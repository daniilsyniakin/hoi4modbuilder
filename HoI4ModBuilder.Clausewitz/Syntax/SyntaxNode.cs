using System.Text;

namespace HoI4ModBuilder.Clausewitz.Syntax;

public abstract class SyntaxNode : SyntaxElement
{
    // Positions are derived on demand rather than cached: game files make millions of nodes, and
    // the first and last token of a node are only a level or two down.

    /// <summary>How many direct children there are (absent optional parts don't count).</summary>
    public abstract int ChildCount { get; }

    /// <summary>A direct child by its index in source order.</summary>
    public abstract SyntaxElement ChildAt(int index);

    /// <summary>Direct children in source order. Absent optional parts are skipped.</summary>
    public IEnumerable<SyntaxElement> Children
    {
        get
        {
            for (var i = 0; i < ChildCount; i++)
                yield return ChildAt(i);
        }
    }

    public override TextSpan FullSpan => TextSpan.FromBounds(FirstToken().FullSpan.Start, LastToken().FullSpan.End);

    /// <summary>From the first real token's text to the last real token's text; missing tokens are ignored.</summary>
    public override TextSpan Span => ComputeSpan();

    // Every node kind contains at least one token, so these always succeed.
    public SyntaxToken FirstToken()
    {
        SyntaxElement element = this;
        while (element is SyntaxNode node)
            element = node.ChildAt(0);
        return (SyntaxToken)element;
    }

    public SyntaxToken LastToken()
    {
        SyntaxElement element = this;
        while (element is SyntaxNode node)
            element = node.ChildAt(node.ChildCount - 1);
        return (SyntaxToken)element;
    }

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

    // Walks below go depth-first with their own stack: nested iterators would cost a step per
    // level for every element, and trees of game files are deep and large.

    public IEnumerable<SyntaxToken> DescendantTokens()
    {
        var stack = new Stack<(SyntaxNode Node, int Next)>();
        stack.Push((this, 0));
        while (stack.Count > 0)
        {
            var (node, next) = stack.Pop();
            if (next >= node.ChildCount)
                continue;
            stack.Push((node, next + 1));
            switch (node.ChildAt(next))
            {
                case SyntaxToken token:
                    yield return token;
                    break;
                case SyntaxNode child:
                    stack.Push((child, 0));
                    break;
            }
        }
    }

    public IEnumerable<SyntaxNode> DescendantNodes()
    {
        var stack = new Stack<(SyntaxNode Node, int Next)>();
        stack.Push((this, 0));
        while (stack.Count > 0)
        {
            var (node, next) = stack.Pop();
            if (next >= node.ChildCount)
                continue;
            stack.Push((node, next + 1));
            if (node.ChildAt(next) is SyntaxNode child)
            {
                yield return child;
                stack.Push((child, 0));
            }
        }
    }

    public override void WriteTo(StringBuilder builder)
    {
        for (var i = 0; i < ChildCount; i++)
            ChildAt(i).WriteTo(builder);
    }

    protected T Adopt<T>(T element) where T : SyntaxElement
    {
        element.Parent = this;
        return element;
    }
}
