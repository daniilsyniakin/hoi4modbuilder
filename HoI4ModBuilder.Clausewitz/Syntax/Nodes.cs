using System.Collections.Immutable;
using System.Text;

namespace HoI4ModBuilder.Clausewitz.Syntax;

/// <summary>Something that holds a list of statements: the file itself or a <c>{ … }</c> block.</summary>
public interface IStatementContainer
{
    ImmutableArray<StatementSyntax> Statements { get; }
}

public static class StatementContainerExtensions
{
    public static IEnumerable<PropertySyntax> Properties(this IStatementContainer container) =>
        container.Statements.OfType<PropertySyntax>();

    /// <summary>All properties with the given key (keys repeat a lot in Clausewitz, e.g. <c>focus</c>).</summary>
    public static IEnumerable<PropertySyntax> Properties(this IStatementContainer container, string key) =>
        container.Properties().Where(p => p.Key.ValueText == key);

    public static PropertySyntax? FindProperty(this IStatementContainer container, string key) =>
        container.Properties(key).FirstOrDefault();

    /// <summary>Bare values of a list block such as <c>{ GER ENG FRA }</c>.</summary>
    public static IEnumerable<ValueSyntax> Elements(this IStatementContainer container) =>
        container.Statements.OfType<ElementSyntax>().Select(e => e.Value);
}

/// <summary>The root: statements followed by the end-of-file token, which carries any trailing trivia.</summary>
public sealed class ScriptFileSyntax : SyntaxNode, IStatementContainer
{
    internal ScriptFileSyntax(ImmutableArray<StatementSyntax> statements, SyntaxToken endOfFile)
    {
        foreach (var statement in statements)
            Adopt(statement);
        Statements = statements;
        EndOfFile = Adopt(endOfFile);
    }

    public override SyntaxKind Kind => SyntaxKind.ScriptFile;

    public ImmutableArray<StatementSyntax> Statements { get; }

    public SyntaxToken EndOfFile { get; }

    public override IEnumerable<SyntaxElement> Children
    {
        get
        {
            foreach (var statement in Statements)
                yield return statement;
            yield return EndOfFile;
        }
    }
}

public abstract class StatementSyntax : SyntaxNode;

/// <summary><c>key = value</c>; the operator is absent in the operator-less form <c>key { … }</c>.</summary>
public sealed class PropertySyntax : StatementSyntax
{
    internal PropertySyntax(ScalarSyntax key, SyntaxToken? @operator, ValueSyntax value)
    {
        Key = Adopt(key);
        Operator = @operator is null ? null : Adopt(@operator);
        Value = Adopt(value);
    }

    public override SyntaxKind Kind => SyntaxKind.Property;

    public ScalarSyntax Key { get; }

    public SyntaxToken? Operator { get; }

    public ValueSyntax Value { get; }

    public override IEnumerable<SyntaxElement> Children
    {
        get
        {
            yield return Key;
            if (Operator is not null)
                yield return Operator;
            yield return Value;
        }
    }
}

/// <summary>A value standing on its own inside a block, e.g. each tag in <c>{ GER ENG }</c>.</summary>
public sealed class ElementSyntax : StatementSyntax
{
    internal ElementSyntax(ValueSyntax value) => Value = Adopt(value);

    public override SyntaxKind Kind => SyntaxKind.Element;

    public ValueSyntax Value { get; }

    public override IEnumerable<SyntaxElement> Children
    {
        get { yield return Value; }
    }
}

/// <summary>Tokens the parser couldn't place, such as a stray <c>}</c>. A diagnostic is reported for them.</summary>
public sealed class SkippedTokensSyntax : StatementSyntax
{
    internal SkippedTokensSyntax(ImmutableArray<SyntaxToken> tokens)
    {
        foreach (var token in tokens)
            Adopt(token);
        Tokens = tokens;
    }

    public override SyntaxKind Kind => SyntaxKind.SkippedTokens;

    public ImmutableArray<SyntaxToken> Tokens { get; }

    public override IEnumerable<SyntaxElement> Children => Tokens;
}

public abstract class ValueSyntax : SyntaxNode;

/// <summary>A single word or quoted string.</summary>
public sealed class ScalarSyntax : ValueSyntax
{
    internal ScalarSyntax(SyntaxToken token) => Token = Adopt(token);

    public override SyntaxKind Kind => SyntaxKind.Scalar;

    public SyntaxToken Token { get; }

    public bool IsQuoted => Token.Kind == SyntaxKind.StringToken;

    /// <summary>The value with quotes removed and <c>\"</c>, <c>\\</c> unescaped.</summary>
    public string ValueText => IsQuoted ? Unquote(Token.Text) : Token.Text;

    public override IEnumerable<SyntaxElement> Children
    {
        get { yield return Token; }
    }

    private static string Unquote(string text)
    {
        // An unterminated string has no closing quote.
        var end = text.Length > 1 && text[^1] == '"' && !IsEscaped(text, text.Length - 1) ? text.Length - 1 : text.Length;
        if (text.IndexOf('\\', 1) < 0)
            return text[1..end];

        var builder = new StringBuilder(end);
        for (var i = 1; i < end; i++)
        {
            if (text[i] == '\\' && i + 1 < end && text[i + 1] is '"' or '\\')
                i++;
            builder.Append(text[i]);
        }
        return builder.ToString();
    }

    private static bool IsEscaped(string text, int index)
    {
        var backslashes = 0;
        for (var i = index - 1; i > 0 && text[i] == '\\'; i--)
            backslashes++;
        return backslashes % 2 == 1;
    }
}

/// <summary><c>{ … }</c>. The closing brace is a missing token when the block is never closed.</summary>
public sealed class BlockSyntax : ValueSyntax, IStatementContainer
{
    internal BlockSyntax(SyntaxToken openBrace, ImmutableArray<StatementSyntax> statements, SyntaxToken closeBrace)
    {
        OpenBrace = Adopt(openBrace);
        foreach (var statement in statements)
            Adopt(statement);
        Statements = statements;
        CloseBrace = Adopt(closeBrace);
    }

    public override SyntaxKind Kind => SyntaxKind.Block;

    public SyntaxToken OpenBrace { get; }

    public ImmutableArray<StatementSyntax> Statements { get; }

    public SyntaxToken CloseBrace { get; }

    public override IEnumerable<SyntaxElement> Children
    {
        get
        {
            yield return OpenBrace;
            foreach (var statement in Statements)
                yield return statement;
            yield return CloseBrace;
        }
    }
}

/// <summary>A block prefixed by a word on the same line, e.g. <c>color = rgb { 255 0 0 }</c>.</summary>
public sealed class TaggedBlockSyntax : ValueSyntax
{
    internal TaggedBlockSyntax(SyntaxToken tag, BlockSyntax block)
    {
        Tag = Adopt(tag);
        Block = Adopt(block);
    }

    public override SyntaxKind Kind => SyntaxKind.TaggedBlock;

    public SyntaxToken Tag { get; }

    public BlockSyntax Block { get; }

    public override IEnumerable<SyntaxElement> Children
    {
        get
        {
            yield return Tag;
            yield return Block;
        }
    }
}
