using System.Text;

namespace HoI4ModBuilder.Clausewitz.Syntax;

public abstract class SyntaxNode : SyntaxElement
{
    // Nodes are immutable, so derived positions are computed once.
    private SyntaxToken? _firstToken;
    private SyntaxToken? _lastToken;
    private TextSpan? _span;

    /// <summary>Direct children in source order. Absent optional parts are skipped.</summary>
    public abstract IEnumerable<SyntaxElement> Children { get; }

    public override TextSpan FullSpan => TextSpan.FromBounds(FirstToken().FullSpan.Start, LastToken().FullSpan.End);

    /// <summary>From the first real token's text to the last real token's text; missing tokens are ignored.</summary>
    public override TextSpan Span => _span ??= ComputeSpan();

    // Every node kind contains at least one token, so these always succeed.
    public SyntaxToken FirstToken() => _firstToken ??= Children.First() switch
    {
        SyntaxToken token => token,
        var node => ((SyntaxNode)node).FirstToken(),
    };

    public SyntaxToken LastToken() => _lastToken ??= Children.Last() switch
    {
        SyntaxToken token => token,
        var node => ((SyntaxNode)node).LastToken(),
    };

    private TextSpan ComputeSpan()
    {
        // Missing tokens only occur at the edges (an absent value or closing brace),
        // so the common case needs no scan.
        var first = FirstToken();
        var last = LastToken();
        if (!first.IsMissing && !last.IsMissing)
            return TextSpan.FromBounds(first.Span.Start, last.Span.End);

        var real = DescendantTokens().Where(t => !t.IsMissing).ToList();
        return real.Count == 0
            ? new TextSpan(first.Position, 0)
            : TextSpan.FromBounds(real[0].Span.Start, real[^1].Span.End);
    }

    public IEnumerable<SyntaxToken> DescendantTokens()
    {
        foreach (var child in Children)
        {
            if (child is SyntaxToken token)
            {
                yield return token;
            }
            else
            {
                foreach (var descendant in ((SyntaxNode)child).DescendantTokens())
                    yield return descendant;
            }
        }
    }

    public IEnumerable<SyntaxNode> DescendantNodes()
    {
        foreach (var child in Children)
        {
            if (child is not SyntaxNode node)
                continue;
            yield return node;
            foreach (var descendant in node.DescendantNodes())
                yield return descendant;
        }
    }

    public override void WriteTo(StringBuilder builder)
    {
        foreach (var child in Children)
            child.WriteTo(builder);
    }

    protected T Adopt<T>(T element) where T : SyntaxElement
    {
        element.Parent = this;
        return element;
    }
}
