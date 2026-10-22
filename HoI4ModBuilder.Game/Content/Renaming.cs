using System.Text.RegularExpressions;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Game.Content;

/// <summary>
/// Renaming entities: either just the definition, or the definition and every reference to it
/// (a whole-token match, so <c>UKR_army</c> never touches <c>UKR_army_reform</c>).
/// </summary>
public static partial class Renaming
{
    /// <summary>Kinds whose id is an <c>id = …</c> property; for the rest it is the key of the definition.</summary>
    public static bool HasIdProperty(ContentKind kind) =>
        kind is ContentKind.FocusTree or ContentKind.Focus or ContentKind.Event or ContentKind.State;

    /// <summary>States are numbered; their ids are plain numbers that appear everywhere, so they aren't renamed.</summary>
    public static bool CanRename(ContentKind kind) => kind != ContentKind.State;

    /// <summary>The edit that changes the id where the entity is defined, or null if it isn't in this file.</summary>
    public static TextEdit? RenameDefinition(SyntaxTree tree, string relativePath, ContentKind kind, string oldId, string newId)
    {
        if (ContentParser.Locate(tree, relativePath, kind, oldId) is not { } property)
            return null;
        if (!HasIdProperty(kind))
            return Replace(property.Key, newId);
        return property.Value is BlockSyntax block && ScriptRead.All(block, "id").LastOrDefault() is { Value: ScalarSyntax } id
            ? ScriptEdits.ReplaceValue(id, newId)
            : null;
    }

    /// <summary>Every key, value and list item spelled exactly <paramref name="oldId"/>.</summary>
    public static IEnumerable<TextEdit> RenameReferences(SyntaxTree tree, string oldId, string newId) =>
        tree.Root.DescendantNodes()
            .OfType<ScalarSyntax>()
            .Where(s => !s.Token.IsMissing && s.ValueText == oldId)
            .Select(s => Replace(s, newId));

    /// <summary>Localisation keys of the entity in a .yml file: <c>id:</c> and <c>id_desc:</c>.</summary>
    public static IEnumerable<TextEdit> RenameLocalisationKeys(string text, string oldId, string newId)
    {
        foreach (Match match in LocalisationKey().Matches(text))
        {
            var key = match.Groups["key"];
            if (key.Value == oldId || key.Value == oldId + "_desc")
                yield return new TextEdit(new TextSpan(key.Index, oldId.Length), newId);
        }
    }

    /// <summary>An id the game will accept: one word, no quotes or braces.</summary>
    public static bool IsValidId(string id) =>
        id.Length > 0 && ScriptEdits.FormatValue(id) == id;

    private static TextEdit Replace(ScalarSyntax scalar, string value) =>
        new(scalar.Span, scalar.IsQuoted ? ScriptEdits.Quote(value) : value);

    [GeneratedRegex(@"^[ \t]*(?<key>[^\s:#""]+):", RegexOptions.Multiline)]
    private static partial Regex LocalisationKey();
}
