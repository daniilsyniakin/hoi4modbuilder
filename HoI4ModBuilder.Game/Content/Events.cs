using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Game.Content;

/// <param name="Index">0-based among the event's options; identifies the option for editing.</param>
/// <param name="Name">Localisation key of the button text.</param>
/// <param name="Effects">A short, human-readable outline of what the option does.</param>
/// <param name="FiredEvents">Events this option triggers, in order.</param>
public sealed record EventOption(int Index, string? Name, IReadOnlyList<string> Effects, IReadOnlyList<string> FiredEvents, int Position);

/// <param name="Type">The defining key: <c>country_event</c>, <c>news_event</c>…</param>
/// <param name="ImmediateEvents">Events fired from <c>immediate</c>, before the player picks anything.</param>
public sealed record EventDefinition(
    string Id,
    string Type,
    string? Title,
    string? Description,
    string? Picture,
    bool IsTriggeredOnly,
    bool IsHidden,
    IReadOnlyList<string> ImmediateEvents,
    IReadOnlyList<EventOption> Options,
    int Position);

/// <summary>Something in the mod that fires an event.</summary>
/// <param name="SourceKind">What does it: a focus, a decision, another event…</param>
/// <param name="Detail">For events: which option (its name key), or "immediate".</param>
/// <param name="Position">Offset of the firing statement.</param>
public sealed record EventTrigger(ContentKind SourceKind, string SourceId, string? Detail, int? OptionIndex, string EventId, int Position);

/// <summary>Reads events and finds the effects that fire them, for the event graph.</summary>
public static class Events
{
    private static readonly HashSet<string> FiringKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "country_event", "news_event", "state_event", "unit_leader_event", "operative_leader_event",
    };

    // Not effects: they decide when and how the option shows up.
    private static readonly HashSet<string> OptionMetaKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "ai_chance", "trigger", "original_recipient_only",
    };

    public static EventDefinition? Read(PropertySyntax property)
    {
        if (property.Value is not BlockSyntax block || ScriptRead.Scalar(block, "id") is not { } id)
            return null;

        var options = ScriptRead.All(block, "option")
            .Select((option, index) => ReadOption(option, index))
            .ToList();
        return new EventDefinition(
            id,
            property.Key.ValueText,
            ScriptRead.ScalarOrNested(block, "title", "text"),
            ScriptRead.ScalarOrNested(block, "desc", "text"),
            ScriptRead.Scalar(block, "picture"),
            ScriptRead.Yes(block, "is_triggered_only"),
            ScriptRead.Yes(block, "hidden"),
            ScriptRead.Blocks(block, "immediate").SelectMany(i => FiredEvents(i).Select(f => f.EventId)).ToList(),
            options,
            property.Span.Start);
    }

    private static EventOption ReadOption(PropertySyntax option, int index)
    {
        if (option.Value is not BlockSyntax block)
            return new EventOption(index, null, [], [], option.Span.Start);
        return new EventOption(
            index,
            ScriptRead.Scalar(block, "name"),
            Summarize(block, OptionMetaKeys),
            FiredEvents(block).Select(f => f.EventId).ToList(),
            option.Span.Start);
    }

    /// <summary>
    /// Event-firing statements anywhere under <paramref name="node"/>: <c>country_event = id</c>,
    /// <c>country_event = { id = … days = … }</c> and the weighted <c>random_events = { 50 = id }</c>.
    /// </summary>
    public static IEnumerable<(string EventId, int Position)> FiredEvents(SyntaxNode node)
    {
        foreach (var property in node.DescendantNodes().OfType<PropertySyntax>())
        {
            var key = property.Key.ValueText;
            if (FiringKeys.Contains(key))
            {
                var id = property.Value switch
                {
                    ScalarSyntax { Token.IsMissing: false } scalar => scalar.ValueText,
                    BlockSyntax block => ScriptRead.Scalar(block, "id"),
                    _ => null,
                };
                if (!string.IsNullOrEmpty(id))
                    yield return (id, property.Span.Start);
            }
            else if (key.Equals("random_events", StringComparison.OrdinalIgnoreCase) && property.Value is BlockSyntax weighted)
            {
                foreach (var choice in weighted.Properties())
                {
                    if (choice.Value is ScalarSyntax { Token.IsMissing: false } scalar && scalar.ValueText.Contains('.'))
                        yield return (scalar.ValueText, choice.Span.Start);
                }
            }
        }
    }

    /// <summary>Everything in one file that fires an event, attributed to the entity it sits in.</summary>
    public static IEnumerable<EventTrigger> FindTriggers(SyntaxTree tree, string relativePath)
    {
        var properties = ContentParser.PropertiesByPosition(tree);
        foreach (var entry in ContentParser.Flatten(ContentParser.Parse(tree, relativePath)))
        {
            // A tree's focuses are entries of their own; scanning the tree too would count them twice.
            if (entry.Kind == ContentKind.FocusTree || !properties.TryGetValue(entry.Position, out var property))
                continue;

            if (entry.Kind == ContentKind.Event && property.Value is BlockSyntax eventBlock)
            {
                foreach (var immediate in ScriptRead.Blocks(eventBlock, "immediate"))
                foreach (var (id, position) in FiredEvents(immediate))
                    yield return new EventTrigger(ContentKind.Event, entry.Id, "immediate", null, id, position);

                var index = 0;
                foreach (var option in ScriptRead.All(eventBlock, "option"))
                {
                    var name = option.Value is BlockSyntax optionBlock ? ScriptRead.Scalar(optionBlock, "name") : null;
                    foreach (var (id, position) in FiredEvents(option))
                        yield return new EventTrigger(ContentKind.Event, entry.Id, name, index, id, position);
                    index++;
                }
                continue;
            }

            foreach (var (id, position) in FiredEvents(property))
                yield return new EventTrigger(entry.Kind, entry.Id, null, null, id, position);
        }
    }

    /// <summary>
    /// One line per statement, e.g. <c>add_political_power = 100</c> or <c>add_ideas = { … }</c>;
    /// event-firing statements are left out (the graph draws them as arrows).
    /// </summary>
    public static IReadOnlyList<string> Summarize(BlockSyntax block, IReadOnlySet<string> skip)
    {
        var lines = new List<string>();
        foreach (var statement in block.Statements)
        {
            if (statement is not PropertySyntax property)
                continue;
            var key = property.Key.ValueText;
            if (skip.Contains(key) || FiringKeys.Contains(key))
                continue;

            lines.Add(property.Value switch
            {
                ScalarSyntax scalar => $"{key} = {scalar.ValueText}",
                BlockSyntax { Statements: [PropertySyntax { Value: ScalarSyntax inner } only] } =>
                    $"{key} = {{ {only.Key.ValueText} = {inner.ValueText} }}",
                _ => $"{key} = {{ … }}",
            });
        }
        return lines;
    }
}
