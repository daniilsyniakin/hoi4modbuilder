using System.Text;

namespace HoI4ModBuilder.Clausewitz.Syntax;

/// <summary>Common base of <see cref="SyntaxNode"/> and <see cref="SyntaxToken"/>.</summary>
public abstract class SyntaxElement
{
    public abstract SyntaxKind Kind { get; }

    public SyntaxNode? Parent { get; internal set; }

    /// <summary>The span including leading and trailing trivia.</summary>
    public abstract TextSpan FullSpan { get; }

    /// <summary>The span without the outermost leading and trailing trivia.</summary>
    public abstract TextSpan Span { get; }

    /// <summary>Appends the exact source text of this element, trivia included.</summary>
    public abstract void WriteTo(StringBuilder builder);

    /// <summary>The exact source text, trivia included. For the root this is the whole file.</summary>
    public string ToFullString()
    {
        var builder = new StringBuilder(FullSpan.Length);
        WriteTo(builder);
        return builder.ToString();
    }

    /// <summary>The source text without the outermost trivia.</summary>
    public override string ToString()
    {
        var full = ToFullString();
        return full.Substring(Span.Start - FullSpan.Start, Span.Length);
    }

    public IEnumerable<SyntaxNode> Ancestors()
    {
        for (var node = Parent; node is not null; node = node.Parent)
            yield return node;
    }
}
