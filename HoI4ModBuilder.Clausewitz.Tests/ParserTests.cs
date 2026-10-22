using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Clausewitz.Tests;

public class ParserTests
{
    [Fact]
    public void SimpleProperty()
    {
        var tree = TreeAssert.ParseLossless("a = b");

        var property = Assert.IsType<PropertySyntax>(Assert.Single(tree.Root.Statements));
        Assert.Equal("a", property.Key.ValueText);
        Assert.Equal(SyntaxKind.EqualsToken, property.Operator!.Kind);
        Assert.Equal("b", Assert.IsType<ScalarSyntax>(property.Value).ValueText);
        Assert.Empty(tree.Diagnostics);
    }

    [Theory]
    [InlineData("=", SyntaxKind.EqualsToken)]
    [InlineData("==", SyntaxKind.EqualsEqualsToken)]
    [InlineData("!=", SyntaxKind.NotEqualsToken)]
    [InlineData("<", SyntaxKind.LessThanToken)]
    [InlineData("<=", SyntaxKind.LessThanEqualsToken)]
    [InlineData(">", SyntaxKind.GreaterThanToken)]
    [InlineData(">=", SyntaxKind.GreaterThanEqualsToken)]
    [InlineData("?=", SyntaxKind.QuestionEqualsToken)]
    public void Operators(string op, SyntaxKind kind)
    {
        foreach (var text in new[] { $"a {op} 1", $"a{op}1" })
        {
            var tree = TreeAssert.ParseLossless(text);
            var property = Assert.IsType<PropertySyntax>(Assert.Single(tree.Root.Statements));
            Assert.Equal(kind, property.Operator!.Kind);
            Assert.Equal("1", ((ScalarSyntax)property.Value).ValueText);
        }
    }

    [Fact]
    public void NestedBlocks()
    {
        var tree = TreeAssert.ParseLossless("focus = {\n\tid = UKR_x\n\tcost = 10\n\tprerequisite = { focus = UKR_a }\n}");

        var focus = (BlockSyntax)tree.Root.FindProperty("focus")!.Value;
        Assert.Equal("UKR_x", ((ScalarSyntax)focus.FindProperty("id")!.Value).ValueText);
        Assert.Equal("10", ((ScalarSyntax)focus.FindProperty("cost")!.Value).ValueText);
        var prerequisite = (BlockSyntax)focus.FindProperty("prerequisite")!.Value;
        Assert.Equal("UKR_a", ((ScalarSyntax)prerequisite.FindProperty("focus")!.Value).ValueText);
    }

    [Fact]
    public void RepeatedKeysAreAllKept()
    {
        var tree = TreeAssert.ParseLossless("focus = { id = a } focus = { id = b } focus = { id = c }");
        Assert.Equal(3, tree.Root.Properties("focus").Count());
    }

    [Fact]
    public void ListOfValues()
    {
        var tree = TreeAssert.ParseLossless("tags = { GER \"ENG\" FRA }");

        var block = (BlockSyntax)tree.Root.FindProperty("tags")!.Value;
        var values = block.Elements().Cast<ScalarSyntax>().ToList();
        Assert.Equal(["GER", "ENG", "FRA"], values.Select(v => v.ValueText));
        Assert.True(values[1].IsQuoted);
        Assert.False(values[0].IsQuoted);
    }

    [Fact]
    public void TaggedBlock()
    {
        var tree = TreeAssert.ParseLossless("color = rgb { 255 0 0 }");

        var tagged = Assert.IsType<TaggedBlockSyntax>(tree.Root.FindProperty("color")!.Value);
        Assert.Equal("rgb", tagged.Tag.Text);
        Assert.Equal(3, tagged.Block.Elements().Count());
    }

    [Fact]
    public void BlockOnNextLineIsNotATag()
    {
        var tree = TreeAssert.ParseLossless("a = b\n{ c }");

        Assert.Collection(tree.Root.Statements,
            s => Assert.IsType<ScalarSyntax>(Assert.IsType<PropertySyntax>(s).Value),
            s => Assert.IsType<BlockSyntax>(Assert.IsType<ElementSyntax>(s).Value));
    }

    [Fact]
    public void OperatorlessProperty()
    {
        var tree = TreeAssert.ParseLossless("key { a = 1 }");

        var property = Assert.IsType<PropertySyntax>(Assert.Single(tree.Root.Statements));
        Assert.Null(property.Operator);
        Assert.Equal("1", ((ScalarSyntax)((BlockSyntax)property.Value).FindProperty("a")!.Value).ValueText);
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void TrailingTriviaEndsAtFirstLineBreak()
    {
        var tree = TreeAssert.ParseLossless("a = b # same line\n# own line\nc = d");

        var tokens = tree.Root.DescendantTokens().ToList();
        var b = tokens.Single(t => t.Text == "b");
        var c = tokens.Single(t => t.Text == "c");

        Assert.Equal(
            [SyntaxKind.WhitespaceTrivia, SyntaxKind.CommentTrivia, SyntaxKind.EndOfLineTrivia],
            b.TrailingTrivia.Select(t => t.Kind));
        Assert.Equal("# same line", b.TrailingTrivia[1].Text);
        Assert.Equal([SyntaxKind.CommentTrivia, SyntaxKind.EndOfLineTrivia], c.LeadingTrivia.Select(t => t.Kind));
        Assert.Equal("# own line", c.LeadingTrivia[0].Text);
    }

    [Fact]
    public void TriviaAtEndOfFileBelongsToEndOfFileToken()
    {
        var tree = TreeAssert.ParseLossless("a = b\n\n# tail\n");

        Assert.Equal(
            [SyntaxKind.EndOfLineTrivia, SyntaxKind.CommentTrivia, SyntaxKind.EndOfLineTrivia],
            tree.Root.EndOfFile.LeadingTrivia.Select(t => t.Kind));
    }

    [Fact]
    public void CrLfIsOneLineBreak()
    {
        var tree = TreeAssert.ParseLossless("a = b\r\nc = d");
        var b = tree.Root.DescendantTokens().Single(t => t.Text == "b");
        Assert.Equal("\r\n", Assert.Single(b.TrailingTrivia).Text);
    }

    [Fact]
    public void ByteOrderMarkIsTrivia()
    {
        var tree = TreeAssert.ParseLossless("﻿a = b");
        Assert.Equal("a", ((PropertySyntax)tree.Root.Statements[0]).Key.ValueText);
    }

    [Fact]
    public void StringsKeepHashesAndUnescape()
    {
        var tree = TreeAssert.ParseLossless("a = \"say \\\"hi\\\" \\\\ # not a comment\"");

        var value = (ScalarSyntax)tree.Root.FindProperty("a")!.Value;
        Assert.Equal("say \"hi\" \\ # not a comment", value.ValueText);
        Assert.DoesNotContain(tree.Root.DescendantTokens().SelectMany(t => t.TrailingTrivia),
            t => t.Kind == SyntaxKind.CommentTrivia);
    }

    [Fact]
    public void StringsMaySpanLines()
    {
        var tree = TreeAssert.ParseLossless("a = \"one\ntwo\" b = c");
        Assert.Equal("one\ntwo", ((ScalarSyntax)tree.Root.FindProperty("a")!.Value).ValueText);
        Assert.NotNull(tree.Root.FindProperty("b"));
    }

    [Theory]
    [InlineData("t = var:[?global.x]", "var:[?global.x]")]
    [InlineData("d = 1936.1.1.12", "1936.1.1.12")]
    [InlineData("n = -0.25", "-0.25")]
    [InlineData("s = FROM.FROM.owner", "FROM.FROM.owner")]
    [InlineData("m = @[ cost * 2 ]", "@[ cost * 2 ]")]
    [InlineData("x = !yes", "!yes")]
    public void WordsWithSpecialCharacters(string text, string expected)
    {
        var tree = TreeAssert.ParseLossless(text);
        var property = Assert.IsType<PropertySyntax>(Assert.Single(tree.Root.Statements));
        Assert.Equal(expected, ((ScalarSyntax)property.Value).ValueText);
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void SpanExcludesOuterTrivia()
    {
        var tree = TreeAssert.ParseLossless("  # lead\n  key = value  # trail\n");

        var property = tree.Root.Statements[0];
        Assert.Equal("key = value", property.ToString());
        Assert.Equal(tree.Text, tree.Root.ToFullString());
        Assert.Equal("value", tree.Text.Substring(property.Span.End - 5, 5));
    }

    // ── Error recovery ──────────────────────────────────────

    [Fact]
    public void MissingValue()
    {
        var tree = TreeAssert.ParseLossless("a = ");

        var property = (PropertySyntax)tree.Root.Statements[0];
        Assert.True(((ScalarSyntax)property.Value).Token.IsMissing);
        Assert.Equal(DiagnosticCodes.MissingValue, Assert.Single(tree.Diagnostics).Code);
    }

    [Fact]
    public void MissingValueBeforeCloseBraceKeepsBlock()
    {
        var tree = TreeAssert.ParseLossless("x = { a = }\ny = 1");

        var block = (BlockSyntax)tree.Root.FindProperty("x")!.Value;
        Assert.False(block.CloseBrace.IsMissing);
        Assert.NotNull(tree.Root.FindProperty("y"));
        Assert.Equal(DiagnosticCodes.MissingValue, Assert.Single(tree.Diagnostics).Code);
    }

    [Fact]
    public void UnclosedBlock()
    {
        var tree = TreeAssert.ParseLossless("a = { b = c");

        var block = (BlockSyntax)tree.Root.FindProperty("a")!.Value;
        Assert.True(block.CloseBrace.IsMissing);
        Assert.NotNull(block.FindProperty("b"));
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal(DiagnosticCodes.UnclosedBlock, diagnostic.Code);
        Assert.Equal(new TextSpan(4, 1), diagnostic.Span);
    }

    [Fact]
    public void MissingValueBeforeNextPropertyOnNewLine()
    {
        var tree = TreeAssert.ParseLossless("focus = {\n\tcost =\n\tcompletion_reward = { a = 1 }\n}");

        var focus = (BlockSyntax)tree.Root.FindProperty("focus")!.Value;
        Assert.True(((ScalarSyntax)focus.FindProperty("cost")!.Value).Token.IsMissing);
        Assert.IsType<BlockSyntax>(focus.FindProperty("completion_reward")!.Value);
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal(DiagnosticCodes.MissingValue, diagnostic.Code);
        Assert.Equal("=", tree.Text.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
    }

    [Fact]
    public void ValueOnNextLineIsStillAValue()
    {
        var tree = TreeAssert.ParseLossless("cost =\n\t10\nnext = 1");
        Assert.Equal("10", ((ScalarSyntax)tree.Root.FindProperty("cost")!.Value).ValueText);
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void UnclosedBlockIsBlamedOnTheBlockWithWrongIndentation()
    {
        const string text = "focus_tree = {\n\tfocus = {\n\t\tid = a\n\t}\n\tfocus = {\n\t\treward = {\n\t\t\tx = 1\n\t}\n}\n";
        var tree = TreeAssert.ParseLossless(text);

        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal(DiagnosticCodes.UnclosedBlock, diagnostic.Code);
        // The brace after "reward =" is the one never closed.
        Assert.Equal(text.IndexOf("reward = {", StringComparison.Ordinal) + "reward = ".Length, diagnostic.Span.Start);
    }

    [Fact]
    public void StrayCloseBrace()
    {
        var tree = TreeAssert.ParseLossless("a = b } c = d");

        Assert.IsType<SkippedTokensSyntax>(tree.Root.Statements[1]);
        Assert.NotNull(tree.Root.FindProperty("c"));
        Assert.Equal(DiagnosticCodes.UnexpectedCloseBrace, Assert.Single(tree.Diagnostics).Code);
    }

    [Fact]
    public void OperatorWithoutKey()
    {
        var tree = TreeAssert.ParseLossless("= b");

        Assert.IsType<SkippedTokensSyntax>(tree.Root.Statements[0]);
        Assert.IsType<ElementSyntax>(tree.Root.Statements[1]);
        Assert.Equal(DiagnosticCodes.OperatorWithoutKey, Assert.Single(tree.Diagnostics).Code);
    }

    [Fact]
    public void UnterminatedString()
    {
        var tree = TreeAssert.ParseLossless("a = \"abc\nb = c");

        Assert.Equal(DiagnosticCodes.UnterminatedString, Assert.Single(tree.Diagnostics).Code);
        Assert.Equal("abc\nb = c", ((ScalarSyntax)tree.Root.FindProperty("a")!.Value).ValueText);
    }

    [Fact]
    public void UnterminatedInlineMath()
    {
        var tree = TreeAssert.ParseLossless("a = @[ 1 + 2");
        Assert.Equal(DiagnosticCodes.UnterminatedInlineMath, Assert.Single(tree.Diagnostics).Code);
    }

    [Fact]
    public void EmptyInput()
    {
        var tree = TreeAssert.ParseLossless("");
        Assert.Empty(tree.Root.Statements);
        Assert.Equal(SyntaxKind.EndOfFileToken, tree.Root.EndOfFile.Kind);
    }
}
