using System.Globalization;
using System.Text.RegularExpressions;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Game.Schema;

/// <summary>What a block of script contains, which decides which commands make sense in it.</summary>
public enum ScriptContext
{
    /// <summary>Not a script block (e.g. <c>prerequisite = { focus = … }</c>).</summary>
    Plain,
    /// <summary>Things that happen: <c>completion_reward</c>, <c>immediate</c>, an event option…</summary>
    Effect,
    /// <summary>Conditions: <c>available</c>, <c>trigger</c>, <c>limit</c>…</summary>
    Trigger,
    /// <summary>Numeric bonuses: <c>modifier = { stability_factor = 0.1 }</c>.</summary>
    Modifier,
    /// <summary>AI weights and timings: <c>ai_will_do</c>, <c>ai_chance</c>, <c>mean_time_to_happen</c>.</summary>
    Weight,
}

public enum ValueShape
{
    Unknown,
    Number,
    Bool,
    /// <summary>A word: an id, a tag, a flag name…</summary>
    Text,
    Block,
}

/// <param name="Scopes">Where it may be used (COUNTRY, STATE, CHARACTER…); "any" means anywhere.</param>
/// <param name="Description">The game's own explanation (English).</param>
/// <param name="Shape">What the value looks like, from the documented example; Unknown when there is none.</param>
/// <param name="Fields">For block values: the documented fields and their sample values.</param>
/// <param name="Category">For modifiers: the game's category (country, army…).</param>
public sealed record ScriptCommand(
    string Name,
    ScriptContext Context,
    IReadOnlyList<string> Scopes,
    string Description,
    ValueShape Shape,
    IReadOnlyList<(string Key, string Sample, bool Optional)> Fields,
    string? Sample,
    string? Category);

/// <summary>
/// Reads the documentation that ships with the game (<c>documentation/*_documentation.md</c>):
/// every effect, trigger and modifier with its scopes, description and an example.
/// </summary>
public static partial class ScriptDocumentation
{
    public static IEnumerable<ScriptCommand> ParseEffects(string markdown) => ParseCommands(markdown, ScriptContext.Effect);

    public static IEnumerable<ScriptCommand> ParseTriggers(string markdown) => ParseCommands(markdown, ScriptContext.Trigger);

    /// <summary>Modifiers: <c>## name</c>, "Number with N decimal places", "**Categories**: country".</summary>
    public static IEnumerable<ScriptCommand> ParseModifiers(string markdown)
    {
        foreach (var (name, body) in Sections(markdown))
        {
            if (!body.Contains("Number", StringComparison.Ordinal))
                continue;
            var category = CategoryLine().Match(body) is { Success: true } match ? match.Groups["category"].Value.Trim() : null;
            yield return new ScriptCommand(name, ScriptContext.Modifier, [], "", ValueShape.Number, [], "0.1", category);
        }
    }

    private static IEnumerable<ScriptCommand> ParseCommands(string markdown, ScriptContext context)
    {
        foreach (var (name, body) in Sections(markdown))
        {
            var scopes = ScopesLine().Match(body) is { Success: true } scopeMatch
                ? scopeMatch.Groups["scopes"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : [];
            var code = CodeBlock().Matches(body).Select(m => m.Groups["code"].Value).ToList();
            var description = Description(code.FirstOrDefault() ?? "");
            var (shape, fields, sample) = FromExamples(name, code);
            yield return new ScriptCommand(name, context, scopes, description, shape, fields, sample, null);
        }
    }

    /// <summary>The <c>## heading</c> sections that document one command each (not the scope indexes).</summary>
    private static IEnumerable<(string Name, string Body)> Sections(string markdown)
    {
        // A leading line break lets a section on the very first line split like the others.
        var parts = ("\n" + markdown.Replace("\r\n", "\n")).Split("\n## ");
        foreach (var part in parts.Skip(1))
        {
            var newLine = part.IndexOf('\n');
            var name = (newLine < 0 ? part : part[..newLine]).Trim();
            if (name.Length == 0 || name.Contains(' '))
                continue; // "Effects for scope COUNTRY", "Table of Content"
            yield return (name, newLine < 0 ? "" : part[(newLine + 1)..]);
        }
    }

    /// <summary>The prose before the example: lines up to the first one that looks like script.</summary>
    private static string Description(string code)
    {
        var lines = new List<string>();
        foreach (var raw in code.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Contains('=') || line.Contains('{') || line.EndsWith(':') || line.StartsWith("ex", StringComparison.OrdinalIgnoreCase) && line.Length < 12)
                break;
            if (line.Length > 0)
                lines.Add(line.Replace("\\\"", "\""));
        }
        return string.Join(" ", lines);
    }

    /// <summary>The value shape and fields, from the first example property named <paramref name="name"/>.</summary>
    private static (ValueShape Shape, IReadOnlyList<(string, string, bool)> Fields, string? Sample) FromExamples(string name, IEnumerable<string> code)
    {
        foreach (var block in code)
        {
            var tree = SyntaxTree.Parse(block);
            var property = tree.Root.DescendantNodes().OfType<PropertySyntax>().FirstOrDefault(p => p.Key.ValueText == name);
            switch (property?.Value)
            {
                case ScalarSyntax scalar:
                    return (ShapeOf(scalar.ValueText), [], scalar.ValueText);
                case BlockSyntax body:
                {
                    var fields = new List<(string, string, bool)>();
                    var optional = false;
                    foreach (var statement in body.Statements)
                    {
                        // "# Optional Fields:" comments mark everything after them as optional.
                        if (statement.FirstToken().LeadingTrivia.Any(t => t.Kind == SyntaxKind.CommentTrivia && t.Text.Contains("ptional")))
                            optional = true;
                        if (statement is not PropertySyntax field)
                            continue;
                        var sample = field.Value is ScalarSyntax s ? s.ValueText : "{ }";
                        var fieldOptional = optional || field.LastToken().TrailingTrivia.Any(t => t.Kind == SyntaxKind.CommentTrivia && t.Text.Contains("ptional"));
                        fields.Add((field.Key.ValueText, sample, fieldOptional));
                    }
                    return (ValueShape.Block, fields, null);
                }
            }
        }
        return (ValueShape.Unknown, [], null);
    }

    public static ValueShape ShapeOf(string sample) =>
        sample is "yes" or "no" ? ValueShape.Bool
        : decimal.TryParse(sample, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? ValueShape.Number
        : ValueShape.Text;

    // [ \t]: an empty list must not run on into the next line.
    [GeneratedRegex(@"Supported Scopes:[ \t]*(?<scopes>[^\n]*)")]
    private static partial Regex ScopesLine();

    [GeneratedRegex(@"\*\*Categories\*\*:\s*(?<category>[^\n]*)")]
    private static partial Regex CategoryLine();

    [GeneratedRegex("```\n?(?<code>.*?)```", RegexOptions.Singleline)]
    private static partial Regex CodeBlock();
}
