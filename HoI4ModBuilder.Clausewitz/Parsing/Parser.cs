using System.Collections.Immutable;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Clausewitz.Parsing;

/// <summary>
/// Recursive-descent parser producing a concrete syntax tree. It never throws on bad input:
/// unexpected tokens become <see cref="SkippedTokensSyntax"/> and expected-but-absent ones
/// become missing tokens, so every input character stays in the tree.
/// </summary>
/// <remarks>
/// Grammar:
/// <code>
/// file      = statement* EOF
/// statement = property | element | skipped
/// property  = scalar operator value
///           | WORD block                  (operator-less form, same line)
/// element   = value
/// value     = scalar | block | WORD block (tagged, same line, e.g. rgb { … })
/// block     = "{" statement* "}"
/// scalar    = WORD | STRING
/// </code>
/// </remarks>
internal sealed class Parser
{
    private readonly string _text;
    private readonly Lexer _lexer;
    private readonly DiagnosticBag _diagnostics;
    private readonly List<SyntaxToken> _unclosedBraces = [];
    // Statement lists being collected, one per nesting depth, reused from block to block.
    private readonly List<List<StatementSyntax>> _statementLists = [];
    private int _depth;
    private SyntaxToken _current;
    private SyntaxToken? _next;

    public Parser(string text, DiagnosticBag diagnostics)
    {
        _text = text;
        _diagnostics = diagnostics;
        // Tokens are read as the parser goes, with one of lookahead.
        _lexer = new Lexer(text, diagnostics);
        _current = _lexer.Lex();
    }

    private SyntaxToken Current => _current;

    /// <summary>The token after <see cref="Current"/> (the end-of-file token repeats).</summary>
    private SyntaxToken PeekNext() =>
        _next ??= _current.Kind == SyntaxKind.EndOfFileToken ? _current : _lexer.Lex();

    public ScriptFileSyntax ParseFile()
    {
        var statements = ParseStatements(insideBlock: false);
        var file = new ScriptFileSyntax(statements, NextToken());
        ReportUnclosedBlocks(file);
        return file;
    }

    /// <summary>
    /// A missing <c>}</c> is only noticed at the end of the file, and the brace that ends up
    /// unmatched is usually an outer one, far from the actual mistake. Indentation tells the
    /// real story: a block whose <c>}</c> sits at a different indent than the line of its
    /// <c>{</c> most likely stole a brace meant for its parent. The earliest such block is
    /// where the user forgot to close.
    /// </summary>
    private void ReportUnclosedBlocks(ScriptFileSyntax file)
    {
        if (_unclosedBraces.Count == 0)
            return;

        var suspects = file.DescendantNodes()
            .OfType<BlockSyntax>()
            .Where(b => !b.CloseBrace.IsMissing)
            .Where(HasMismatchedIndentation)
            .OrderBy(b => b.CloseBrace.Position)
            .Select(b => b.OpenBrace)
            .Take(_unclosedBraces.Count)
            .ToList();

        // Fall back to the braces that actually reached the end of the file.
        foreach (var brace in suspects.Count > 0 ? suspects : _unclosedBraces)
            _diagnostics.UnclosedBlock(brace.Span);
    }

    private bool HasMismatchedIndentation(BlockSyntax block)
    {
        var open = block.OpenBrace.Position;
        var close = block.CloseBrace.Position;
        var closeLineStart = LineStart(close);
        // Only judge braces that start their own line, on a different line than the opening one.
        if (closeLineStart <= open || !IsBlank(closeLineStart, close))
            return false;
        return !Indentation(LineStart(open)).SequenceEqual(Indentation(closeLineStart));
    }

    private int LineStart(int position)
    {
        var lineBreak = _text.AsSpan(0, position).LastIndexOfAny('\n', '\r');
        return lineBreak + 1;
    }

    private ReadOnlySpan<char> Indentation(int lineStart)
    {
        var end = lineStart;
        while (end < _text.Length && _text[end] is ' ' or '\t')
            end++;
        return _text.AsSpan(lineStart, end - lineStart);
    }

    private bool IsBlank(int start, int end) => _text.AsSpan(start, end - start).Trim(" \t").IsEmpty;

    private SyntaxToken NextToken()
    {
        var token = _current;
        if (token.Kind != SyntaxKind.EndOfFileToken)
        {
            _current = _next ?? _lexer.Lex();
            _next = null;
        }
        return token;
    }

    private ImmutableArray<StatementSyntax> ParseStatements(bool insideBlock)
    {
        if (_statementLists.Count == _depth)
            _statementLists.Add([]);
        var statements = _statementLists[_depth++];
        statements.Clear();
        try
        {
            CollectStatements(statements, insideBlock);
            return statements.Count == 0 ? [] : [.. statements];
        }
        finally
        {
            _depth--;
        }
    }

    private void CollectStatements(List<StatementSyntax> statements, bool insideBlock)
    {
        while (Current.Kind != SyntaxKind.EndOfFileToken)
        {
            var kind = Current.Kind;
            if (kind == SyntaxKind.CloseBraceToken)
            {
                if (insideBlock)
                    break;
                _diagnostics.UnexpectedCloseBrace(Current.Span);
                statements.Add(Skip());
            }
            else if (SyntaxFacts.IsOperator(kind))
            {
                _diagnostics.OperatorWithoutKey(Current.Span, Current.Text);
                statements.Add(Skip());
            }
            else
            {
                statements.Add(ParseStatement());
            }
        }
    }

    private StatementSyntax ParseStatement()
    {
        if (Current.Kind == SyntaxKind.OpenBraceToken)
            return new ElementSyntax(ParseBlock());

        // Only WORD or STRING can reach here; everything else is handled by ParseStatements.
        var key = NextToken();

        if (SyntaxFacts.IsOperator(Current.Kind))
        {
            var op = NextToken();
            return new PropertySyntax(new ScalarSyntax(key), op, ParseValue(op));
        }

        if (key.Kind == SyntaxKind.WordToken && StartsBlockOnSameLine(key))
            return new PropertySyntax(new ScalarSyntax(key), null, ParseBlock());

        return new ElementSyntax(new ScalarSyntax(key));
    }

    private ValueSyntax ParseValue(SyntaxToken op)
    {
        switch (Current.Kind)
        {
            // "cost =" followed by "completion_reward = {" on the next line: the value was
            // forgotten, and the word is really the next property's key.
            case SyntaxKind.WordToken when SyntaxFacts.IsOperator(PeekNext().Kind)
                                           && (op.HasLineBreakInTrailingTrivia || Current.HasLineBreakInLeadingTrivia):
                _diagnostics.MissingValue(op.Span, op.Text);
                return new ScalarSyntax(SyntaxToken.Missing(SyntaxKind.WordToken, Current.FullSpan.Start));
            case SyntaxKind.WordToken:
                var word = NextToken();
                return StartsBlockOnSameLine(word)
                    ? new TaggedBlockSyntax(word, ParseBlock())
                    : new ScalarSyntax(word);
            case SyntaxKind.StringToken:
                return new ScalarSyntax(NextToken());
            case SyntaxKind.OpenBraceToken:
                return ParseBlock();
            default:
                _diagnostics.MissingValue(op.Span, op.Text);
                return new ScalarSyntax(SyntaxToken.Missing(SyntaxKind.WordToken, Current.FullSpan.Start));
        }
    }

    private BlockSyntax ParseBlock()
    {
        var open = NextToken();
        var statements = ParseStatements(insideBlock: true);

        if (Current.Kind == SyntaxKind.CloseBraceToken)
            return new BlockSyntax(open, statements, NextToken());

        // Reported at the end, once we can guess where the brace is really missing.
        _unclosedBraces.Add(open);
        return new BlockSyntax(open, statements, SyntaxToken.Missing(SyntaxKind.CloseBraceToken, Current.FullSpan.Start));
    }

    /// <summary>
    /// <c>rgb { … }</c> / <c>key { … }</c> only bind when the brace is on the same line, so a list
    /// like <c>{ a \n { b } }</c> still parses as two separate elements.
    /// </summary>
    private bool StartsBlockOnSameLine(SyntaxToken previous) =>
        Current.Kind == SyntaxKind.OpenBraceToken
        && !previous.HasLineBreakInTrailingTrivia
        && !Current.HasLineBreakInLeadingTrivia;

    private SkippedTokensSyntax Skip() => new([NextToken()]);
}
