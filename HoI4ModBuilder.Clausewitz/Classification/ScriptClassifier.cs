using System.Text.RegularExpressions;
using HoI4ModBuilder.Clausewitz.Parsing;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Clausewitz.Classification;

/// <summary>A multi-line construct that is still open at the end of a line.</summary>
public enum LineState
{
    Default,
    InString,
    InInlineMath,
}

public enum ScriptClassification
{
    Comment,
    String,
    /// <summary>Numbers, percentages and dates such as <c>1936.1.1</c>.</summary>
    Number,
    /// <summary>The left-hand side of a property.</summary>
    Key,
    /// <summary>Logic and control words: <c>yes</c>, <c>AND</c>, <c>limit</c>, <c>if</c>…</summary>
    Keyword,
    /// <summary>Scope references: <c>ROOT</c>, <c>FROM.FROM</c>, <c>PREV</c>…</summary>
    Scope,
    /// <summary>Script constants and variables: <c>@cost</c>, <c>var:x</c>, <c>[?x]</c>, <c>@[ … ]</c>.</summary>
    Variable,
    /// <summary>Three-letter country tags: <c>GER</c>, <c>D01</c>.</summary>
    Tag,
    Operator,
    Brace,
    /// <summary>Any other word.</summary>
    Value,
}

public readonly record struct ClassifiedSpan(int Start, int Length, ScriptClassification Classification)
{
    public int End => Start + Length;
}

/// <summary>
/// Lexical classification for syntax highlighting. Works one line at a time so an editor can
/// re-colour only what it shows; <see cref="LineState"/> carries strings that span lines.
/// It uses the parser's own lexer, so highlighting and parsing always agree on token boundaries.
/// </summary>
public static partial class ScriptClassifier
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "yes", "no", "AND", "OR", "NOT", "NOR", "NAND",
        "if", "else", "else_if", "limit", "hidden_effect", "hidden_trigger",
        "random_list", "custom_trigger_tooltip", "custom_effect_tooltip", "count_triggers",
        "always", "rgb", "RGB", "hsv", "HSV", "hsv360", "HSV360",
    };

    private static readonly HashSet<string> Scopes = new(StringComparer.Ordinal)
    {
        "ROOT", "THIS", "PREV", "FROM", "OWNER", "CONTROLLER", "CAPITAL", "OVERLORD",
        "FACTION_LEADER", "OCCUPIED", "OWNER_OR_CONTROLLER",
    };

    /// <summary>Classifies one line (without its line break) that starts in <paramref name="startState"/>.</summary>
    /// <returns>The state at the end of the line, to be passed to the next line.</returns>
    public static LineState ClassifyLine(string line, LineState startState, List<ClassifiedSpan> output)
    {
        var lexer = new Lexer(line, new DiagnosticBag(), startState);
        var tokens = new List<SyntaxToken>();
        SyntaxToken token;
        do
        {
            token = lexer.Lex();
            tokens.Add(token);
        } while (token.Kind != SyntaxKind.EndOfFileToken);

        for (var i = 0; i < tokens.Count; i++)
        {
            token = tokens[i];
            AddComments(token.LeadingTrivia, output);

            if (token.Text.Length > 0)
            {
                var previous = i > 0 ? tokens[i - 1].Kind : SyntaxKind.None;
                var next = i + 1 < tokens.Count ? tokens[i + 1].Kind : SyntaxKind.None;
                // A string that started on an earlier line is still a string.
                var classification = i == 0 && startState == LineState.InString
                    ? ScriptClassification.String
                    : Classify(token, previous, next);
                output.Add(new ClassifiedSpan(token.Position, token.Text.Length, classification));
            }

            AddComments(token.TrailingTrivia, output);
        }

        return lexer.EndState;
    }

    /// <summary>
    /// The state at the end of <paramref name="line"/>, without classifying anything. Allocation-free
    /// and much faster than <see cref="ClassifyLine"/>; an editor uses it to skip over the lines
    /// before the visible ones. Mirrors the lexer's rules for strings, comments and <c>@[ ]</c>.
    /// </summary>
    public static LineState ScanLineState(ReadOnlySpan<char> line, LineState state)
    {
        var i = 0;
        while (i < line.Length)
        {
            switch (state)
            {
                case LineState.InString:
                    var c = line[i];
                    if (c == '\\')
                    {
                        i += 2;
                        continue;
                    }
                    i++;
                    if (c == '"')
                        state = LineState.Default;
                    break;

                case LineState.InInlineMath:
                    var close = line[i..].IndexOf(']');
                    if (close < 0)
                        return LineState.InInlineMath;
                    // The rest of the word after "]" belongs to the same token.
                    i += close + 1;
                    while (i < line.Length && IsWordCharacter(line, i))
                        i++;
                    state = LineState.Default;
                    break;

                default:
                    switch (line[i])
                    {
                        case '#':
                            return LineState.Default;
                        case '"':
                            state = LineState.InString;
                            break;
                        // "@[" opens inline math only at the start of a word.
                        case '@' when i + 1 < line.Length && line[i + 1] == '[' && (i == 0 || !IsWordCharacter(line, i - 1)):
                            state = LineState.InInlineMath;
                            i++;
                            break;
                    }
                    i++;
                    break;
            }
        }
        return state;
    }

    // Must match Lexer.IsWordCharacter.
    private static bool IsWordCharacter(ReadOnlySpan<char> line, int i)
    {
        var c = line[i];
        if (c == '﻿' || char.IsWhiteSpace(c) || c is '{' or '}' or '=' or '"' or '#' or '<' or '>')
            return false;
        return !(c is '!' or '?' && i + 1 < line.Length && line[i + 1] == '=');
    }

    /// <summary>Classifies a token given its neighbours (enough to tell keys from values).</summary>
    public static ScriptClassification Classify(SyntaxToken token, SyntaxKind previous, SyntaxKind next)
    {
        switch (token.Kind)
        {
            case SyntaxKind.StringToken:
                return ScriptClassification.String;
            case SyntaxKind.OpenBraceToken or SyntaxKind.CloseBraceToken:
                return ScriptClassification.Brace;
            case var kind when SyntaxFacts.IsOperator(kind):
                return ScriptClassification.Operator;
        }

        var isKey = SyntaxFacts.IsOperator(next)
            || (next == SyntaxKind.OpenBraceToken && !SyntaxFacts.IsOperator(previous));
        return ClassifyWord(token.Text, isKey);
    }

    public static ScriptClassification ClassifyWord(string word, bool isKey)
    {
        if (Keywords.Contains(word))
            return ScriptClassification.Keyword;
        if (IsScopeChain(word))
            return ScriptClassification.Scope;
        if (word.StartsWith('@') || word.Contains("var:", StringComparison.Ordinal) || word.Contains("[?", StringComparison.Ordinal))
            return ScriptClassification.Variable;
        if (NumberRegex().IsMatch(word))
            return ScriptClassification.Number;
        if (TagRegex().IsMatch(word))
            return ScriptClassification.Tag;
        return isKey ? ScriptClassification.Key : ScriptClassification.Value;
    }

    /// <summary><c>ROOT</c>, <c>FROM.FROM</c>, <c>PREV.PREV.OWNER</c>…</summary>
    private static bool IsScopeChain(string word)
    {
        foreach (var part in word.Split('.'))
        {
            if (!Scopes.Contains(part))
                return false;
        }
        return true;
    }

    private static void AddComments(IEnumerable<SyntaxTrivia> trivia, List<ClassifiedSpan> output)
    {
        foreach (var t in trivia)
        {
            if (t.Kind == SyntaxKind.CommentTrivia)
                output.Add(new ClassifiedSpan(t.Position, t.Text.Length, ScriptClassification.Comment));
        }
    }

    // 10, -0.5, 25%, 1936.1.1, 1936.1.1.12
    [GeneratedRegex(@"^-?\d+(\.\d+)*%?$")]
    private static partial Regex NumberRegex();

    [GeneratedRegex("^[A-Z][A-Z0-9]{2}$")]
    private static partial Regex TagRegex();
}
