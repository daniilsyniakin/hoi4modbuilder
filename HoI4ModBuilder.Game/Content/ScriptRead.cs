using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Game.Content;

/// <summary>Small helpers for reading values out of script blocks. Keys are case-insensitive, as in the game.</summary>
public static class ScriptRead
{
    public static IEnumerable<PropertySyntax> All(IStatementContainer container, string key) =>
        container.Properties().Where(p => p.Key.ValueText.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The value of the last <paramref name="key"/> (the one the game uses), if it is a scalar.</summary>
    public static string? Scalar(IStatementContainer container, string key) =>
        All(container, key).LastOrDefault()?.Value is ScalarSyntax { Token.IsMissing: false } scalar ? scalar.ValueText : null;

    public static IEnumerable<BlockSyntax> Blocks(IStatementContainer container, string key) =>
        All(container, key).Select(p => p.Value).OfType<BlockSyntax>();

    public static int Int(IStatementContainer container, string key) =>
        int.TryParse(Scalar(container, key), out var value) ? value : 0;

    public static bool Yes(IStatementContainer container, string key) =>
        string.Equals(Scalar(container, key), "yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>key = value</c> or the conditional form <c>key = { trigger = { … } text = value }</c>
    /// (used for titles, descriptions and icons); the first unconditional-looking value wins.
    /// </summary>
    public static string? ScalarOrNested(IStatementContainer container, string key, string nestedKey)
    {
        foreach (var property in All(container, key))
        {
            switch (property.Value)
            {
                case ScalarSyntax { Token.IsMissing: false } scalar:
                    return scalar.ValueText;
                case BlockSyntax block when Scalar(block, nestedKey) is { } nested:
                    return nested;
            }
        }
        return null;
    }

    /// <summary>The <c>focus = X</c> values of a block such as <c>prerequisite = { focus = A focus = B }</c>.</summary>
    public static IReadOnlyList<string> Values(IStatementContainer container, string key) =>
        All(container, key).Select(p => p.Value).OfType<ScalarSyntax>().Where(s => !s.Token.IsMissing).Select(s => s.ValueText).ToList();
}
