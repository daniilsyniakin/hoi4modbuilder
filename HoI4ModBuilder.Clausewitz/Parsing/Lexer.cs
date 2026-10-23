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

    private LineState _resumeState = initialState;
    private int _position;

    /// <summary>The construct still open at the end of the text, if any.</summary>
    public LineState EndState { get; private set; }

    public SyntaxToken Lex()
    {
        var fullStart = _position;
        var lineBreakBefore = false;
        int start;
        SyntaxKind kind;
        if (_resumeState != LineState.Default)
        {
            // The text starts inside a string or inline math; there is no leading trivia.
            start = _position;
            kind = _resumeState == LineState.InString ? ContinueString() : ContinueInlineMath();
            _resumeState = LineState.Default;
        }
        else
        {
            lineBreakBefore = SkipTrivia(isTrailing: false);
            start = _position;
            kind = ScanToken();
        }
        // Punctuation has fixed text; words repeat a lot (keys above all), so they are shared too.
        var tokenText = SyntaxFacts.GetText(kind) ?? Words.Get(text, start, _position - start);
        // The end-of-file token keeps everything in its leading trivia.
        var lineBreakAfter = kind != SyntaxKind.EndOfFileToken && SkipTrivia(isTrailing: true);
        return new SyntaxToken(kind, start, tokenText, text, fullStart, _position, lineBreakBefore, lineBreakAfter);
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

    /// <summary>Moves past whitespace, comments and line breaks; trailing trivia stops after the first line break.</summary>
    /// <returns>Whether a line break was passed.</returns>
    private bool SkipTrivia(bool isTrailing)
    {
        var lineBreak = false;
        while (!AtEnd)
        {
            var c = Current;
            if (c is '\r' or '\n')
            {
                _position += c == '\r' && Peek(1) == '\n' ? 2 : 1;
                lineBreak = true;
                if (isTrailing)
                    break;
            }
            else if (IsWhitespace(c))
            {
                while (!AtEnd && IsWhitespace(Current))
                    _position++;
            }
            else if (c == '#')
            {
                while (!AtEnd && Current is not ('\r' or '\n'))
                    _position++;
            }
            else
            {
                break;
            }
        }
        return lineBreak;
    }

    /// <summary>The trivia between <paramref name="start"/> and <paramref name="end"/>, split the way the lexer reads it.</summary>
    internal static ImmutableArray<SyntaxTrivia> SplitTrivia(string text, int start, int end)
    {
        if (start >= end)
            return [];
        var trivia = ImmutableArray.CreateBuilder<SyntaxTrivia>();
        var position = start;
        while (position < end)
        {
            var from = position;
            var c = text[position];
            SyntaxKind kind;
            if (c is '\r' or '\n')
            {
                position += c == '\r' && position + 1 < end && text[position + 1] == '\n' ? 2 : 1;
                kind = SyntaxKind.EndOfLineTrivia;
            }
            else if (IsWhitespace(c))
            {
                while (position < end && IsWhitespace(text[position]))
                    position++;
                kind = SyntaxKind.WhitespaceTrivia;
            }
            else
            {
                while (position < end && text[position] is not ('\r' or '\n'))
                    position++;
                kind = SyntaxKind.CommentTrivia;
            }
            trivia.Add(new SyntaxTrivia(kind, from, text.Substring(from, position - from)));
        }
        return trivia.ToImmutable();
    }

    // Spaces and tabs first: they are nearly all the whitespace there is.
    private static bool IsWhitespace(char c) =>
        c is ' ' or '\t' || c == ByteOrderMark || (c < ' ' || c > '~') && char.IsWhiteSpace(c) && c is not ('\r' or '\n');

    /// <summary>
    /// A small shared table of recent words, so a key that appears thousands of times (<c>id</c>,
    /// <c>focus</c>, <c>add_ideas</c>) is one string rather than thousands. Races only cost a miss.
    /// </summary>
    private static class Words
    {
        private const int MaxLength = 48;
        private const int Size = 1 << 14;
        private static readonly string?[] Table = new string?[Size];

        public static string Get(string text, int start, int length)
        {
            if (length == 0)
                return "";
            var span = text.AsSpan(start, length);
            if (length > MaxLength)
                return new string(span);
            var slot = string.GetHashCode(span) & (Size - 1);
            var known = Table[slot];
            if (known is not null && span.SequenceEqual(known))
                return known;
            var created = new string(span);
            Table[slot] = created;
            return created;
        }
    }
}
