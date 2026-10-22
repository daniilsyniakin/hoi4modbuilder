namespace HoI4ModBuilder.Clausewitz.Syntax;

public enum SyntaxKind
{
    None,

    // Tokens
    EndOfFileToken,
    /// <summary>Any unquoted run of characters: keys, numbers, dates, <c>yes</c>, <c>GER</c>, <c>@const</c>, <c>var:x</c>…</summary>
    WordToken,
    /// <summary>A double-quoted string, quotes included.</summary>
    StringToken,
    OpenBraceToken,
    CloseBraceToken,
    EqualsToken,
    EqualsEqualsToken,
    NotEqualsToken,
    LessThanToken,
    LessThanEqualsToken,
    GreaterThanToken,
    GreaterThanEqualsToken,
    /// <summary><c>?=</c> — "equals if the left side exists".</summary>
    QuestionEqualsToken,

    // Trivia
    WhitespaceTrivia,
    EndOfLineTrivia,
    /// <summary><c># …</c> up to (not including) the line break.</summary>
    CommentTrivia,

    // Nodes
    ScriptFile,
    /// <summary><c>key = value</c>, <c>key &gt; value</c>, or <c>key { … }</c> without an operator.</summary>
    Property,
    /// <summary>A bare value inside a block, e.g. the items of <c>{ GER ENG }</c>.</summary>
    Element,
    Scalar,
    Block,
    /// <summary>A block prefixed with a word: <c>rgb { 255 0 0 }</c>.</summary>
    TaggedBlock,
    /// <summary>Tokens that don't fit the grammar; kept so the tree stays lossless.</summary>
    SkippedTokens,
}

public static class SyntaxFacts
{
    public static bool IsOperator(SyntaxKind kind) => kind is
        SyntaxKind.EqualsToken or SyntaxKind.EqualsEqualsToken or SyntaxKind.NotEqualsToken
        or SyntaxKind.LessThanToken or SyntaxKind.LessThanEqualsToken
        or SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken
        or SyntaxKind.QuestionEqualsToken;

    public static bool IsTrivia(SyntaxKind kind) => kind is
        SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia or SyntaxKind.CommentTrivia;

    /// <summary>The fixed text of punctuation tokens, or <c>null</c> for tokens with variable text.</summary>
    public static string? GetText(SyntaxKind kind) => kind switch
    {
        SyntaxKind.OpenBraceToken => "{",
        SyntaxKind.CloseBraceToken => "}",
        SyntaxKind.EqualsToken => "=",
        SyntaxKind.EqualsEqualsToken => "==",
        SyntaxKind.NotEqualsToken => "!=",
        SyntaxKind.LessThanToken => "<",
        SyntaxKind.LessThanEqualsToken => "<=",
        SyntaxKind.GreaterThanToken => ">",
        SyntaxKind.GreaterThanEqualsToken => ">=",
        SyntaxKind.QuestionEqualsToken => "?=",
        _ => null,
    };
}
