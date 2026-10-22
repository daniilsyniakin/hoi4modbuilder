using HoI4ModBuilder.Clausewitz.Parsing;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Clausewitz;

/// <summary>
/// A parsed Clausewitz script file. The tree is lossless:
/// <c>SyntaxTree.Parse(text).Root.ToFullString() == text</c> holds for any input, including broken files.
/// </summary>
public sealed class SyntaxTree
{
    private SyntaxTree(string text, ScriptFileSyntax root, IReadOnlyList<Diagnostic> diagnostics)
    {
        Text = text;
        Root = root;
        Diagnostics = diagnostics;
    }

    public string Text { get; }

    public ScriptFileSyntax Root { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public static SyntaxTree Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var diagnostics = new DiagnosticBag();
        var root = new Parser(text, diagnostics).ParseFile();
        return new SyntaxTree(text, root, diagnostics.Items.OrderBy(d => d.Span.Start).ToList());
    }
}
