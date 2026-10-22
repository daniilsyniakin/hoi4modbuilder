using System.Collections.Immutable;
using System.Text;

namespace HoI4ModBuilder.Clausewitz.Syntax;

/// <summary>
/// A token with its trivia. Trailing trivia runs up to and including the first line break;
/// everything after that belongs to the next token's leading trivia.
/// </summary>
public sealed class SyntaxToken : SyntaxElement
{
    internal SyntaxToken(
        SyntaxKind kind,
        int position,
        string text,
        ImmutableArray<SyntaxTrivia> leadingTrivia,
        ImmutableArray<SyntaxTrivia> trailingTrivia,
        bool isMissing = false)
    {
        Kind = kind;
        Position = position;
        Text = text;
        LeadingTrivia = leadingTrivia;
        TrailingTrivia = trailingTrivia;
        IsMissing = isMissing;
    }

    /// <summary>Creates a zero-width placeholder for a token the parser expected but didn't find.</summary>
    internal static SyntaxToken Missing(SyntaxKind kind, int position) =>
        new(kind, position, "", [], [], isMissing: true);

    public override SyntaxKind Kind { get; }

    /// <summary>Start of <see cref="Text"/> (after the leading trivia).</summary>
    public int Position { get; }

    public string Text { get; }

    public ImmutableArray<SyntaxTrivia> LeadingTrivia { get; }

    public ImmutableArray<SyntaxTrivia> TrailingTrivia { get; }

    /// <summary>True for placeholders inserted by error recovery; they have no text.</summary>
    public bool IsMissing { get; }

    public override TextSpan Span => new(Position, Text.Length);

    public override TextSpan FullSpan => TextSpan.FromBounds(
        Position - Width(LeadingTrivia),
        Position + Text.Length + Width(TrailingTrivia));

    public bool HasLineBreakInLeadingTrivia => LeadingTrivia.Any(t => t.Kind == SyntaxKind.EndOfLineTrivia);

    public bool HasLineBreakInTrailingTrivia => TrailingTrivia.Any(t => t.Kind == SyntaxKind.EndOfLineTrivia);

    public override void WriteTo(StringBuilder builder)
    {
        foreach (var trivia in LeadingTrivia)
            builder.Append(trivia.Text);
        builder.Append(Text);
        foreach (var trivia in TrailingTrivia)
            builder.Append(trivia.Text);
    }

    public override string ToString() => Text;

    private static int Width(ImmutableArray<SyntaxTrivia> trivia)
    {
        var width = 0;
        foreach (var t in trivia)
            width += t.Text.Length;
        return width;
    }
}
