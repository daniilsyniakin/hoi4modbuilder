using System.Text.RegularExpressions;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Game.Content;

/// <summary>A place where an id is written.</summary>
/// <param name="Start">Offset of the id in the file.</param>
/// <param name="Line">1-based line.</param>
/// <param name="Owner">The entity it is written in (the focus whose reward adds an idea); null outside any.</param>
/// <param name="Context">The keys leading to it inside the owner: <c>completion_reward › add_ideas</c>.</param>
/// <param name="IsDefinition">Where the entity itself is defined, not a use of it.</param>
/// <param name="Snippet">The line, trimmed.</param>
public sealed record Usage(int Start, int Line, ContentEntry? Owner, string Context, bool IsDefinition, string Snippet);

/// <summary>Finds where an id is used: as a key, a value or a list item in scripts, as a key in localisation.</summary>
public static partial class Usages
{
    private const int MaxSnippet = 140;

    public static IEnumerable<Usage> InScript(SyntaxTree tree, string relativePath, string id)
    {
        if (!tree.Text.Contains(id, StringComparison.Ordinal))
            yield break;
        var properties = ContentParser.PropertiesByPosition(tree);
        var owners = ContentParser.Flatten(ContentParser.Parse(tree, relativePath))
            .Select(e => (Entry: e, Property: properties.GetValueOrDefault(e.Position)))
            .Where(o => o.Property is not null)
            .ToList();

        foreach (var scalar in tree.Root.DescendantNodes().OfType<ScalarSyntax>())
        {
            if (scalar.Token.IsMissing || scalar.ValueText != id)
                continue;
            var start = scalar.Span.Start;
            // The innermost entity around it: a focus rather than its tree.
            var owner = owners
                .Where(o => o.Property!.Span.Start <= start && start < o.Property.Span.End)
                .OrderBy(o => o.Property!.Span.Length)
                .FirstOrDefault();

            var keys = new List<string>();
            foreach (var ancestor in scalar.Ancestors())
            {
                if (ancestor == owner.Property)
                    break;
                if (ancestor is PropertySyntax property && property.Key != scalar)
                    keys.Add(property.Key.ValueText);
            }
            keys.Reverse();

            var isDefinition = owner.Entry is { } entry && entry.Id == id
                && (owner.Property!.Key == scalar
                    || scalar.Parent is PropertySyntax { Key.ValueText: "id" } idProperty && idProperty.Parent?.Parent == owner.Property);
            yield return new Usage(start, LineOf(tree.Text, start), owner.Entry, string.Join(" › ", keys), isDefinition,
                Snippet(tree.Text, start));
        }
    }

    /// <summary>Localisation keys of the id in a .yml file: <c>id:</c>, <c>id_desc:</c> and the like.</summary>
    public static IEnumerable<Usage> InLocalisation(string text, string id)
    {
        if (!text.Contains(id, StringComparison.Ordinal))
            yield break;
        foreach (Match match in LocalisationKey().Matches(text))
        {
            var key = match.Groups["key"];
            if (key.Value == id || key.Value.StartsWith(id + "_", StringComparison.Ordinal) || key.Value.StartsWith(id + ".", StringComparison.Ordinal))
                yield return new Usage(key.Index, LineOf(text, key.Index), null, key.Value, false, Snippet(text, key.Index));
        }
    }

    private static int LineOf(string text, int offset)
    {
        var line = 1;
        for (var i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
                line++;
        }
        return line;
    }

    private static string Snippet(string text, int offset)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
        var end = text.IndexOf('\n', offset);
        var line = text[start..(end < 0 ? text.Length : end)].Trim();
        return line.Length > MaxSnippet ? line[..MaxSnippet] + "…" : line;
    }

    [GeneratedRegex(@"^[ \t]*(?<key>[^\s:#""]+):", RegexOptions.Multiline)]
    private static partial Regex LocalisationKey();
}
