using Avalonia.Media;
using Avalonia.Media.Immutable;
using HoI4ModBuilder.Clausewitz.Classification;

namespace HoI4ModBuilder.Editor;

/// <summary>Syntax colours, following the JetBrains "Dark" editor scheme.</summary>
public static class EditorColors
{
    private static readonly IBrush Comment = Brush("#7A7E85");
    private static readonly IBrush String = Brush("#6AAB73");
    private static readonly IBrush Number = Brush("#2AACB8");
    private static readonly IBrush Key = Brush("#C77DBB");
    private static readonly IBrush Keyword = Brush("#CF8E6D");
    private static readonly IBrush Scope = Brush("#B3AE60");
    private static readonly IBrush Variable = Brush("#56A8F5");
    private static readonly IBrush Tag = Brush("#D5B778");
    private static readonly IBrush Punctuation = Brush("#BCBEC4");

    public static readonly Color Error = Color.Parse("#F75464");
    public static readonly Color Warning = Color.Parse("#E5B85C");

    /// <returns>Null for plain values, which keep the editor's default foreground.</returns>
    public static IBrush? For(ScriptClassification classification) => classification switch
    {
        ScriptClassification.Comment => Comment,
        ScriptClassification.String => String,
        ScriptClassification.Number => Number,
        ScriptClassification.Key => Key,
        ScriptClassification.Keyword => Keyword,
        ScriptClassification.Scope => Scope,
        ScriptClassification.Variable => Variable,
        ScriptClassification.Tag => Tag,
        ScriptClassification.Operator or ScriptClassification.Brace => Punctuation,
        _ => null,
    };

    private static IBrush Brush(string hex) => new ImmutableSolidColorBrush(Color.Parse(hex));
}
