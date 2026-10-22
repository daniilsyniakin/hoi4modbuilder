namespace HoI4ModBuilder.Clausewitz.Syntax;

/// <summary>Whitespace, a line break or a comment attached to a token.</summary>
public readonly record struct SyntaxTrivia(SyntaxKind Kind, int Position, string Text)
{
    public TextSpan Span => new(Position, Text.Length);

    public override string ToString() => Text;
}
