using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Clausewitz.Tests;

internal static class TreeAssert
{
    /// <summary>Parses and checks every losslessness invariant, throwing on the first violation.</summary>
    public static SyntaxTree ParseLossless(string text)
    {
        var tree = SyntaxTree.Parse(text);
        if (FindViolation(tree) is { } violation)
            Assert.Fail(violation);
        return tree;
    }

    /// <summary>Returns a description of the first broken invariant, or null if the tree is sound.</summary>
    public static string? FindViolation(SyntaxTree tree)
    {
        var text = tree.Text;

        if (tree.Root.ToFullString() != text)
            return "Dump differs from input.";

        // Tokens with their trivia must tile the text exactly, in order.
        var expected = 0;
        foreach (var token in tree.Root.DescendantTokens())
        {
            if (token.FullSpan.Start != expected)
                return $"Gap or overlap before token '{token.Text}' at {token.FullSpan.Start}, expected {expected}.";
            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                if (string.CompareOrdinal(text, trivia.Position, trivia.Text, 0, trivia.Text.Length) != 0)
                    return $"Trivia {trivia.Kind} at {trivia.Position} doesn't match the source.";
            }
            if (string.CompareOrdinal(text, token.Position, token.Text, 0, token.Text.Length) != 0)
                return $"Token {token.Kind} at {token.Position} doesn't match the source.";
            if (token.IsMissing && (token.Text.Length != 0 || token.FullSpan.Length != 0))
                return $"Missing token {token.Kind} at {token.Position} has width.";
            expected = token.FullSpan.End;
        }
        if (expected != text.Length)
            return $"Tokens end at {expected}, text has {text.Length} characters.";

        return FindStructureViolation(tree.Root);
    }

    private static string? FindStructureViolation(SyntaxNode node)
    {
        var nodeSpan = node.FullSpan;
        foreach (var child in node.Children)
        {
            if (!ReferenceEquals(child.Parent, node))
                return $"{child.Kind} at {child.FullSpan} has the wrong parent.";
            if (!nodeSpan.Contains(child.FullSpan))
                return $"{child.Kind} {child.FullSpan} lies outside its parent {node.Kind} {nodeSpan}.";
            if (child is SyntaxNode childNode && FindStructureViolation(childNode) is { } violation)
                return violation;
        }
        return null;
    }
}
