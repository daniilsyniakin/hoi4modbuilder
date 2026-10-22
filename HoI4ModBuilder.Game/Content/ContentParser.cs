using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Game.Content;

/// <summary>Game entities a mod defines, as opposed to the files they live in.</summary>
public enum ContentKind
{
    FocusTree,
    Focus,
    Event,
    Idea,
    Decision,
    Technology,
    Character,
    Country,
    State,
    ScriptedEffect,
    ScriptedTrigger,
}

/// <param name="Id">The name scripts refer to: focus id, event id, idea name, country tag…</param>
/// <param name="Group">Sub-folder in the content view: event namespace, idea slot, decision category…</param>
/// <param name="Detail">Extra hint shown next to the id, e.g. a state's name key.</param>
/// <param name="Position">Offset of the definition in its file, for go-to-definition.</param>
/// <param name="Children">Nested entities, e.g. the focuses of a focus tree.</param>
public sealed record ContentEntry(
    ContentKind Kind,
    string Id,
    string? Group,
    string? Detail,
    int Position,
    IReadOnlyList<ContentEntry> Children);

/// <summary>
/// Finds entity definitions in script files. Each kind lives in a fixed folder that the game reads
/// non-recursively, so the folder of a file decides how it is read.
/// </summary>
public static class ContentParser
{
    private delegate IEnumerable<ContentEntry> FileParser(IStatementContainer root, string fileName);

    private static readonly Dictionary<string, FileParser> Parsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["common/national_focus"] = ParseFocusTrees,
        ["events"] = ParseEvents,
        ["common/ideas"] = ParseIdeas,
        ["common/decisions"] = ParseDecisions,
        ["common/technologies"] = ParseTechnologies,
        ["common/characters"] = ParseCharacters,
        ["common/country_tags"] = ParseCountryTags,
        ["history/states"] = ParseStates,
        ["common/scripted_effects"] = (root, _) => ParseTopLevel(root, ContentKind.ScriptedEffect),
        ["common/scripted_triggers"] = (root, _) => ParseTopLevel(root, ContentKind.ScriptedTrigger),
    };

    private static readonly HashSet<string> EventKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "country_event", "news_event", "state_event", "unit_leader_event", "operative_leader_event",
    };

    /// <summary>Folders (relative to the mod root, with <c>/</c>) that hold content.</summary>
    public static IEnumerable<string> Folders => Parsers.Keys;

    /// <summary>True if the game would read <paramref name="relativePath"/> as one of the content kinds.</summary>
    public static bool IsContentFile(string relativePath) =>
        Path.GetExtension(relativePath).Equals(".txt", StringComparison.OrdinalIgnoreCase)
        && Parsers.ContainsKey(FolderOf(relativePath));

    /// <summary>The entities defined in one file; empty for files outside the content folders.</summary>
    public static IReadOnlyList<ContentEntry> Parse(SyntaxTree tree, string relativePath) =>
        Parsers.TryGetValue(FolderOf(relativePath), out var parser)
            ? parser(tree.Root, Path.GetFileNameWithoutExtension(relativePath)).ToList()
            : [];

    /// <summary>
    /// The syntax of an entity in a freshly parsed file, found by kind and id; the
    /// <paramref name="occurrence"/>-th one (0-based) when the id is defined more than once.
    /// </summary>
    public static PropertySyntax? Locate(SyntaxTree tree, string relativePath, ContentKind kind, string id, int occurrence = 0)
    {
        var entry = Flatten(Parse(tree, relativePath))
            .Where(e => e.Kind == kind && e.Id == id)
            .Skip(occurrence)
            .FirstOrDefault();
        return entry is null ? null : PropertiesByPosition(tree).GetValueOrDefault(entry.Position);
    }

    /// <summary>Every property of the file by its start offset, for mapping entries back to syntax.</summary>
    public static Dictionary<int, PropertySyntax> PropertiesByPosition(SyntaxTree tree) =>
        tree.Root.DescendantNodes().OfType<PropertySyntax>().ToDictionary(p => p.Span.Start);

    /// <summary>Entries and their children (focuses of trees), depth first.</summary>
    public static IEnumerable<ContentEntry> Flatten(IEnumerable<ContentEntry> entries) =>
        entries.SelectMany(e => Flatten(e.Children).Prepend(e));

    private static string FolderOf(string relativePath) =>
        (Path.GetDirectoryName(relativePath) ?? "").Replace('\\', '/');

    private static IEnumerable<ContentEntry> ParseFocusTrees(IStatementContainer root, string fileName)
    {
        foreach (var property in BlockProperties(root))
        {
            var key = property.Key.ValueText;
            var block = (BlockSyntax)property.Value;
            if (key.Equals("focus_tree", StringComparison.OrdinalIgnoreCase))
            {
                var focuses = Blocks(block, "focus")
                    .Select(f => Entry(ContentKind.Focus, Scalar(f.Block, "id"), null, null, f.Property))
                    .OfType<ContentEntry>()
                    .ToList();
                yield return new ContentEntry(ContentKind.FocusTree, Scalar(block, "id") ?? fileName, null, null,
                    property.Span.Start, focuses);
            }
            else if (key.Equals("shared_focus", StringComparison.OrdinalIgnoreCase)
                     && Entry(ContentKind.Focus, Scalar(block, "id"), null, null, property) is { } shared)
            {
                // Shared focuses belong to no tree until one pulls them in.
                yield return shared;
            }
        }
    }

    private static IEnumerable<ContentEntry> ParseEvents(IStatementContainer root, string fileName)
    {
        foreach (var property in BlockProperties(root))
        {
            if (!EventKeys.Contains(property.Key.ValueText))
                continue;
            var id = Scalar((BlockSyntax)property.Value, "id");
            var dot = id?.LastIndexOf('.') ?? -1;
            var group = dot > 0 ? id![..dot] : fileName;
            if (Entry(ContentKind.Event, id, group, null, property) is { } entry)
                yield return entry;
        }
    }

    private static IEnumerable<ContentEntry> ParseIdeas(IStatementContainer root, string fileName)
    {
        foreach (var ideas in Blocks(root, "ideas"))
        foreach (var slot in BlockProperties(ideas.Block))
        foreach (var idea in BlockProperties((BlockSyntax)slot.Value))
            yield return Entry(ContentKind.Idea, idea.Key.ValueText, slot.Key.ValueText, null, idea)!;
    }

    private static IEnumerable<ContentEntry> ParseDecisions(IStatementContainer root, string fileName)
    {
        foreach (var category in BlockProperties(root))
        foreach (var decision in BlockProperties((BlockSyntax)category.Value))
            yield return Entry(ContentKind.Decision, decision.Key.ValueText, category.Key.ValueText, null, decision)!;
    }

    private static IEnumerable<ContentEntry> ParseTechnologies(IStatementContainer root, string fileName)
    {
        // One file per branch (infantry, armor…), so the file name is the natural group.
        foreach (var technologies in Blocks(root, "technologies"))
        foreach (var tech in BlockProperties(technologies.Block))
            yield return Entry(ContentKind.Technology, tech.Key.ValueText, fileName, null, tech)!;
    }

    private static IEnumerable<ContentEntry> ParseCharacters(IStatementContainer root, string fileName)
    {
        foreach (var characters in Blocks(root, "characters"))
        foreach (var character in BlockProperties(characters.Block))
            yield return Entry(ContentKind.Character, character.Key.ValueText, null, null, character)!;
    }

    private static IEnumerable<ContentEntry> ParseCountryTags(IStatementContainer root, string fileName)
    {
        // TAG = "countries/Name.txt"; other keys (dynamic_tags = yes) are settings.
        foreach (var property in root.Properties())
        {
            if (property.Value is ScalarSyntax { Token.IsMissing: false } path && path.ValueText.Contains('/'))
                yield return Entry(ContentKind.Country, property.Key.ValueText, null, path.ValueText, property)!;
        }
    }

    private static IEnumerable<ContentEntry> ParseStates(IStatementContainer root, string fileName)
    {
        foreach (var state in Blocks(root, "state"))
        {
            var entry = Entry(ContentKind.State, Scalar(state.Block, "id"), null, Scalar(state.Block, "name"), state.Property);
            if (entry is not null)
                yield return entry;
        }
    }

    private static IEnumerable<ContentEntry> ParseTopLevel(IStatementContainer root, ContentKind kind) =>
        BlockProperties(root).Select(p => Entry(kind, p.Key.ValueText, null, null, p)!);

    private static ContentEntry? Entry(ContentKind kind, string? id, string? group, string? detail, PropertySyntax property) =>
        string.IsNullOrEmpty(id) ? null : new ContentEntry(kind, id, group, detail, property.Span.Start, []);

    /// <summary><c>key = { … }</c> properties, skipping settings and <c>@variables</c>.</summary>
    private static IEnumerable<PropertySyntax> BlockProperties(IStatementContainer container) =>
        container.Properties().Where(p => p.Value is BlockSyntax && !p.Key.Token.IsMissing && !p.Key.ValueText.StartsWith('@'));

    private static IEnumerable<(PropertySyntax Property, BlockSyntax Block)> Blocks(IStatementContainer container, string key) =>
        BlockProperties(container)
            .Where(p => p.Key.ValueText.Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(p => (p, (BlockSyntax)p.Value));

    private static string? Scalar(IStatementContainer container, string key) =>
        container.Properties()
            .LastOrDefault(p => p.Key.ValueText.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value is ScalarSyntax { Token.IsMissing: false } scalar
            ? scalar.ValueText
            : null;
}
