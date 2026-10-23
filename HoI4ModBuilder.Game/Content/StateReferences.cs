using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Game.Content;

/// <summary>What a script does with a state.</summary>
public enum StateAction
{
    /// <summary>Gives it to another country (<c>transfer_state</c>, <c>set_state_owner</c>…).</summary>
    Transfer,
    Core,
    Claim,
    Building,
    /// <summary>Checks who owns or controls it (<c>owns_state</c>, <c>is_owned_by</c>…).</summary>
    OwnerCheck,
    /// <summary>Anything else done in or with it.</summary>
    Other,
}

/// <summary>A mention of a state in a script.</summary>
/// <param name="Country">The country named with it (<c>add_core_of = GER</c>), when there is one.</param>
public sealed record StateReference(int State, StateAction Action, string Key, string? Country, Usage Usage);

/// <summary>
/// Finds where scripts deal with states: effects and triggers taking a state id
/// (<c>transfer_state = 64</c>) and state scopes (<c>64 = { add_core_of = UKR }</c>).
/// </summary>
public static class StateReferences
{
    // key = <state id> or key = { state = <state id> }
    private static readonly Dictionary<string, StateAction> ById = new(StringComparer.OrdinalIgnoreCase)
    {
        ["transfer_state"] = StateAction.Transfer,
        ["add_state_core"] = StateAction.Core,
        ["remove_state_core"] = StateAction.Core,
        ["add_state_claim"] = StateAction.Claim,
        ["remove_state_claim"] = StateAction.Claim,
        ["owns_state"] = StateAction.OwnerCheck,
        ["controls_state"] = StateAction.OwnerCheck,
        ["has_full_control_of_state"] = StateAction.OwnerCheck,
        ["set_capital"] = StateAction.Other,
        ["capital"] = StateAction.Other,
        ["add_resource"] = StateAction.Other,
        ["has_state_flag"] = StateAction.Other,
    };

    // Inside a state scope: key = <country>
    private static readonly Dictionary<string, StateAction> InScope = new(StringComparer.OrdinalIgnoreCase)
    {
        ["transfer_state_to"] = StateAction.Transfer,
        ["set_state_owner"] = StateAction.Transfer,
        ["set_state_controller"] = StateAction.Transfer,
        ["set_state_owner_to"] = StateAction.Transfer,
        ["set_state_controller_to"] = StateAction.Transfer,
        ["add_core_of"] = StateAction.Core,
        ["remove_core_of"] = StateAction.Core,
        ["add_claim_by"] = StateAction.Claim,
        ["remove_claim_by"] = StateAction.Claim,
        ["add_building_construction"] = StateAction.Building,
        ["add_extra_state_shared_building_slots"] = StateAction.Building,
        ["remove_building"] = StateAction.Building,
        ["damage_building"] = StateAction.Building,
        ["set_building_level"] = StateAction.Building,
        ["is_owned_by"] = StateAction.OwnerCheck,
        ["is_controlled_by"] = StateAction.OwnerCheck,
        ["is_owned_and_controlled_by"] = StateAction.OwnerCheck,
        ["is_fully_controlled_by"] = StateAction.OwnerCheck,
        ["is_core_of"] = StateAction.OwnerCheck,
        ["is_claimed_by"] = StateAction.OwnerCheck,
    };

    // Number-keyed blocks that are not states.
    private static readonly HashSet<string> NotStateParents = new(StringComparer.OrdinalIgnoreCase)
    {
        "random_list", "buildings", "provinces", "victory_points", "random", "ai_chance",
    };

    /// <summary>Whether <c>key = …</c> takes a state id: <c>transfer_state</c>, <c>owns_state</c>, <c>state</c>…</summary>
    public static bool TakesStateId(string key) => ById.ContainsKey(key) || key.Equals("state", StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<StateReference> Find(SyntaxTree tree, string relativePath)
    {
        var text = tree.Text;
        var properties = ContentParser.PropertiesByPosition(tree);
        var owners = ContentParser.Flatten(ContentParser.Parse(tree, relativePath))
            .Select(e => (Entry: e, Property: properties.GetValueOrDefault(e.Position)))
            .Where(o => o.Property is not null)
            .ToList();

        foreach (var property in tree.Root.DescendantNodes().OfType<PropertySyntax>())
        {
            var key = property.Key.ValueText;
            if (ById.TryGetValue(key, out var action))
            {
                var value = property.Value switch
                {
                    ScalarSyntax scalar => scalar.ValueText,
                    BlockSyntax block => ScriptRead.Scalar(block, "state"),
                    _ => null,
                };
                if (int.TryParse(value, out var state))
                    yield return Reference(state, action, key, null, property, property);
                continue;
            }

            // A state scope: 64 = { … }
            if (property.Value is not BlockSyntax scope || !int.TryParse(key, out var scoped) || scoped <= 0
                || property.Parent?.Parent is PropertySyntax parent && NotStateParents.Contains(parent.Key.ValueText))
                continue;
            var found = false;
            foreach (var inner in scope.Properties())
            {
                if (!InScope.TryGetValue(inner.Key.ValueText, out var innerAction))
                    continue;
                found = true;
                var country = (inner.Value as ScalarSyntax)?.ValueText;
                // Tags are three capitals or digits; ROOT, FROM and the like are scopes, not countries.
                var tag = country is { Length: 3 } && country.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)) ? country : null;
                yield return Reference(scoped, innerAction, inner.Key.ValueText, tag,
                    property, inner);
            }
            if (!found && scope.Statements.Length > 0)
                yield return Reference(scoped, StateAction.Other, key, null, property, property);
        }

        StateReference Reference(int state, StateAction action, string key, string? country, PropertySyntax at, PropertySyntax inner)
        {
            var start = at.Span.Start;
            var owner = owners
                .Where(o => o.Property!.Span.Start <= start && start < o.Property.Span.End && o.Property != at)
                .OrderBy(o => o.Property!.Span.Length)
                .FirstOrDefault();
            var keys = new List<string>();
            foreach (var ancestor in inner.Ancestors().Prepend(inner))
            {
                if (ancestor == owner.Property)
                    break;
                if (ancestor is PropertySyntax p)
                    keys.Add(p.Key.ValueText);
            }
            keys.Reverse();
            var line = 1 + text.AsSpan(0, Math.Min(start, text.Length)).Count('\n');
            var lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
            var lineEnd = text.IndexOf('\n', start);
            var snippet = text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)].Trim();
            if (snippet.Length > 140)
                snippet = snippet[..140] + "…";
            return new StateReference(state, action, key, country,
                new Usage(start, line, owner.Entry, string.Join(" › ", keys), false, snippet));
        }
    }
}
