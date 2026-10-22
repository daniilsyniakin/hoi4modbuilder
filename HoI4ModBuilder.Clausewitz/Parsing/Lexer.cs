using System.Collections.Immutable;
using HoI4ModBuilder.Clausewitz.Classification;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Clausewitz.Parsing;

/// <summary>
/// Splits Clausewitz script into tokens. Every character of the input ends up either in a
/// token's text or in its trivia, which is what makes the tree lossless.
/// </summary>
/// <param name="initialState">
/// Lets line-by-line consumers (syntax highlighting) start in the middle of a construct that
/// began on an earlier line.
/// </param>
internal sealed class Lexer(string text, DiagnosticBag diagnostics, LineState initialState = LineState.Default)
{
    private const char ByteOrderMark = '﻿';

    private readonly ImmutableArray<SyntaxTrivia>.Builder _triviaBuffer = ImmutableArray.CreateBuilder<SyntaxTrivia>();
    private LineState _resumeState = initialState;
    private int _position;

    /// <summary>The construct still open at the end of the text, if any.</summary>
    public LineState EndState { get; private set; }

    public SyntaxToken Lex()
    {
        ImmutableArray<SyntaxTrivia> leading;
        int start;
        SyntaxKind kind;
        if (_resumeState != LineState.Default)
        {
            // The text starts inside a string or inline math; there is no leading trivia.
            leading = [];
            start = _position;
            kind = _resumeState == LineState.InString ? ContinueString() : ContinueInlineMath();
            _resumeState = LineState.Default;
        }
        else
        {
            leading = ScanTrivia(isTrailing: false);
            start = _position;
            kind = ScanToken();
        }
        // Punctuation has fixed text; sharing it avoids an allocation per brace and operator.
        var tokenText = SyntaxFacts.GetText(kind) ?? Slice(start);
        // The end-of-file token keeps everything in its leading trivia.
        var trailing = kind == SyntaxKind.EndOfFileToken ? [] : ScanTrivia(isTrailing: true);
        return new SyntaxToken(kind, start, tokenText, leading, trailing);
    }

    private char Current => Peek(0);

    private char Peek(int offset) =>
        _position + offset < text.Length ? text[_position + offset] : '\0';

    private bool AtEnd => _position >= text.Length;

    private SyntaxKind ScanToken()
    {
        if (AtEnd)
            return SyntaxKind.EndOfFileToken;

        switch (Current)
        {
            case '{':
                _position++;
                return SyntaxKind.OpenBraceToken;
            case '}':
                _position++;
                return SyntaxKind.CloseBraceToken;
            case '=':
                return Operator(Peek(1) == '=' ? SyntaxKind.EqualsEqualsToken : SyntaxKind.EqualsToken);
            case '<':
                return Operator(Peek(1) == '=' ? SyntaxKind.LessThanEqualsToken : SyntaxKind.LessThanToken);
            case '>':
                return Operator(Peek(1) == '=' ? SyntaxKind.GreaterThanEqualsToken : SyntaxKind.GreaterThanToken);
            case '!' when Peek(1) == '=':
                return Operator(SyntaxKind.NotEqualsToken);
            case '?' when Peek(1) == '=':
                return Operator(SyntaxKind.QuestionEqualsToken);
            case '"':
                ScanString();
                return SyntaxKind.StringToken;
            default:
                ScanWord();
                return SyntaxKind.WordToken;
        }
    }

    private SyntaxKind Operator(SyntaxKind kind)
    {
        _position += SyntaxFacts.GetText(kind)!.Length;
        return kind;
    }

    private void ScanString()
    {
        var start = _position;
        _position++; // opening quote
        ScanStringBody(start);
    }

    private SyntaxKind ContinueString()
    {
        ScanStringBody(_position);
        return SyntaxKind.StringToken;
    }

    /// <summary>Strings may span lines; <c>\"</c> and <c>\\</c> are escapes.</summary>
    private void ScanStringBody(int start)
    {
        while (true)
        {
            if (AtEnd)
            {
                EndState = LineState.InString;
                diagnostics.UnterminatedString(TextSpan.FromBounds(start, _position));
                return;
            }

            var c = Current;
            if (c == '\\' && _position + 1 < text.Length)
            {
                _position += 2;
            }
            else
            {
                _position++;
                if (c == '"')
                    return;
            }
        }
    }

    private void ScanWord()
    {
        // Inline math, e.g. @[ base_cost * 2 ], may contain spaces and operators.
        if (Current == '@' && Peek(1) == '[')
        {
            var start = _position;
            _position += 2;
            if (!ScanInlineMathBody(start))
                return;
        }

        while (!AtEnd && IsWordCharacter())
            _position++;
    }

    private SyntaxKind ContinueInlineMath()
    {
        if (ScanInlineMathBody(_position))
        {
            while (!AtEnd && IsWordCharacter())
                _position++;
        }
        return SyntaxKind.WordToken;
    }

    /// <returns>False if the closing bracket is missing and the text was consumed to the end.</returns>
    private bool ScanInlineMathBody(int start)
    {
        var close = text.IndexOf(']', _position);
        if (close < 0)
        {
            _position = text.Length;
            EndState = LineState.InInlineMath;
            diagnostics.UnterminatedInlineMath(TextSpan.FromBounds(start, _position));
            return false;
        }
        _position = close + 1;
        return true;
    }

    private bool IsWordCharacter()
    {
        var c = Current;
        if (IsWhitespace(c) || c is '\r' or '\n' or '{' or '}' or '=' or '"' or '#' or '<' or '>')
            return false;
        // '!' and '?' are ordinary characters unless they start "!=" / "?=".
        return !(c is '!' or '?' && Peek(1) == '=');
    }

    private ImmutableArray<SyntaxTrivia> ScanTrivia(bool isTrailing)
    {
        var trivia = _triviaBuffer;
        trivia.Clear();

        while (!AtEnd)
        {
            var start = _position;
            var c = Current;
            SyntaxKind kind;

            if (c is '\r' or '\n')
            {
                _position += c == '\r' && Peek(1) == '\n' ? 2 : 1;
                kind = SyntaxKind.EndOfLineTrivia;
            }
            else if (IsWhitespace(c))
            {
                while (!AtEnd && IsWhitespace(Current))
                    _position++;
                kind = SyntaxKind.WhitespaceTrivia;
            }
            else if (c == '#')
            {
                while (!AtEnd && Current is not ('\r' or '\n'))
                    _position++;
                kind = SyntaxKind.CommentTrivia;
            }
            else
            {
                break;
            }

            trivia.Add(new SyntaxTrivia(kind, start, Slice(start)));

            // Trailing trivia stops after the first line break.
            if (isTrailing && kind == SyntaxKind.EndOfLineTrivia)
                break;
        }

        return trivia.Count == 0 ? [] : trivia.ToImmutable();
    }

    /// <summary>
    /// Text from <paramref name="start"/> to the current position. Line breaks and short
    /// indentation are by far the most common trivia, so they are shared instead of allocated.
    /// </summary>
    private string Slice(int start)
    {
        var length = _position - start;
        var span = text.AsSpan(start, length);
        switch (length)
        {
            case 1:
                switch (span[0])
                {
                    case '\n': return "\n";
                    case '\r': return "\r";
                    case ' ': return " ";
                    case '\t': return "\t";
                }
                break;
            case 2 when span is "\r\n":
                return "\r\n";
        }
        if (length <= MaxCachedIndent && span.Trim('\t').IsEmpty)
            return Tabs[length];
        if (length <= MaxCachedIndent && span.Trim(' ').IsEmpty)
            return Spaces[length];
        return text.Substring(start, length);
    }

    private const int MaxCachedIndent = 16;
    private static readonly string[] Tabs = Enumerable.Range(0, MaxCachedIndent + 1).Select(n => new string('\t', n)).ToArray();
    private static readonly string[] Spaces = Enumerable.Range(0, MaxCachedIndent + 1).Select(n => new string(' ', n)).ToArray();

    private static bool IsWhitespace(char c) =>
        c == ByteOrderMark || (char.IsWhiteSpace(c) && c is not ('\r' or '\n'));
}
