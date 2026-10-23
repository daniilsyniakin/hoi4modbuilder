using System.Collections.Immutable;
using System.Text;
using HoI4ModBuilder.Clausewitz.Parsing;

namespace HoI4ModBuilder.Clausewitz.Syntax;

/// <summary>
/// A token with its trivia. Trailing trivia runs up to and including the first line break;
/// everything after that belongs to the next token's leading trivia.
/// </summary>
/// <remarks>
/// Trivia is kept as bounds in the source text and only split into pieces when asked for: almost
/// nothing reads it, and a list per token would be most of what parsing allocates.
/// </remarks>
public sealed class SyntaxToken : SyntaxElement
{
    private readonly string? _source;
    private readonly int _fullStart;
    private readonly int _fullEnd;
    private readonly bool _lineBreakBefore;
    private readonly bool _lineBreakAfter;

    internal SyntaxToken(SyntaxKind kind, int position, string text, string? source, int fullStart, int fullEnd,
        bool lineBreakBefore, bool lineBreakAfter, bool isMissing = false)
    {
        Kind = kind;
        Position = position;
        Text = text;
        _source = source;
        _fullStart = fullStart;
        _fullEnd = fullEnd;
        _lineBreakBefore = lineBreakBefore;
        _lineBreakAfter = lineBreakAfter;
        IsMissing = isMissing;
    }

    /// <summary>Creates a zero-width placeholder for a token the parser expected but didn't find.</summary>
    internal static SyntaxToken Missing(SyntaxKind kind, int position) =>
        new(kind, position, "", null, position, position, false, false, isMissing: true);

    public override SyntaxKind Kind { get; }

    /// <summary>Start of <see cref="Text"/> (after the leading trivia).</summary>
    public int Position { get; }

    public string Text { get; }

    public ImmutableArray<SyntaxTrivia> LeadingTrivia =>
        _source is null ? [] : Lexer.SplitTrivia(_source, _fullStart, Position);

    public ImmutableArray<SyntaxTrivia> TrailingTrivia =>
        _source is null ? [] : Lexer.SplitTrivia(_source, Position + Text.Length, _fullEnd);

    /// <summary>True for placeholders inserted by error recovery; they have no text.</summary>
    public bool IsMissing { get; }

    public override TextSpan Span => new(Position, Text.Length);

    public override TextSpan FullSpan => TextSpan.FromBounds(_fullStart, _fullEnd);

    public bool HasLineBreakInLeadingTrivia => _lineBreakBefore;

    public bool HasLineBreakInTrailingTrivia => _lineBreakAfter;

    public override void WriteTo(StringBuilder builder)
    {
        if (_source is null)
        {
            builder.Append(Text);
            return;
        }
        var end = Position + Text.Length;
        builder.Append(_source, _fullStart, Position - _fullStart).Append(Text).Append(_source, end, _fullEnd - end);
    }

    public override string ToString() => Text;
}
