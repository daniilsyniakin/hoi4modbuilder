using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Clausewitz.Tests;

public sealed class ScriptEditsTests
{
    private static BlockSyntax Block(SyntaxTree tree, string key) =>
        (BlockSyntax)tree.Root.DescendantNodes().OfType<PropertySyntax>().First(p => p.Key.ValueText == key).Value;

    private static string Edit(string text, Func<SyntaxTree, TextEdit> build)
    {
        var tree = SyntaxTree.Parse(text);
        var result = ScriptEdits.Apply(text, [build(tree)]);
        Assert.False(SyntaxTree.Parse(result).HasErrors, result);
        return result;
    }

    [Fact]
    public void ReplacesValueKeepingEverythingElse()
    {
        const string text = "focus = {\n\tid = A # the start\n\tx = 3\n}\n";

        var result = Edit(text, t => ScriptEdits.SetProperty(text, Block(t, "focus"), "x", "5"));

        Assert.Equal("focus = {\n\tid = A # the start\n\tx = 5\n}\n", result);
    }

    [Fact]
    public void KeepsQuotesWhenTheValueWasQuoted()
    {
        const string text = "a = { name = \"Old\" }";

        var result = Edit(text, t => ScriptEdits.SetProperty(text, Block(t, "a"), "name", "New \"one\""));

        Assert.Equal("a = { name = \"New \\\"one\\\"\" }", result);
    }

    [Fact]
    public void QuotesValuesThatAreNotWords()
    {
        Assert.Equal("GFX_goal", ScriptEdits.FormatValue("GFX_goal"));
        Assert.Equal("\"two words\"", ScriptEdits.FormatValue("two words"));
        Assert.Equal("\"\"", ScriptEdits.FormatValue(""));
    }

    [Fact]
    public void AddsPropertyOnItsOwnLineWithTheSameIndentation()
    {
        const string text = "focus = {\r\n\tid = A\r\n\tx = 1 # pos\r\n}\r\n";

        var result = Edit(text, t => ScriptEdits.SetProperty(text, Block(t, "focus"), "y", "2"));

        Assert.Equal("focus = {\r\n\tid = A\r\n\tx = 1 # pos\r\n\ty = 2\r\n}\r\n", result);
    }

    [Fact]
    public void AddsPropertyInsideOneLineBlock()
    {
        Assert.Equal("f = { id = A y = 2 }",
            Edit("f = { id = A }", t => ScriptEdits.SetProperty("f = { id = A }", Block(t, "f"), "y", "2")));
        Assert.Equal("f = { y = 2 }",
            Edit("f = {}", t => ScriptEdits.SetProperty("f = {}", Block(t, "f"), "y", "2")));
    }

    [Fact]
    public void AddsPropertyToEmptyMultiLineBlock()
    {
        const string text = "tree = {\n\tf = {\n\t}\n}";

        var result = Edit(text, t => ScriptEdits.SetProperty(text, Block(t, "f"), "y", "2"));

        Assert.Equal("tree = {\n\tf = {\n\t\ty = 2\n\t}\n}", result);
    }

    [Fact]
    public void AddsPropertyAtTheEndOfTheFile()
    {
        Assert.Equal("a = 1\nb = 2\n", Edit("a = 1\n", t => ScriptEdits.SetProperty("a = 1\n", t.Root, "b", "2")));
    }

    [Fact]
    public void RemovesWholeLineWithItsComment()
    {
        const string text = "f = {\n\tid = A\n\tx = 1 # gone\n\ty = 2\n}";

        var result = Edit(text, t => ScriptEdits.RemoveStatement(text, Block(t, "f").FindProperty("x")!));

        Assert.Equal("f = {\n\tid = A\n\ty = 2\n}", result);
    }

    [Fact]
    public void RemovesInlineStatementOnly()
    {
        const string text = "f = { id = A x = 1 y = 2 }";

        var result = Edit(text, t => ScriptEdits.RemoveStatement(text, Block(t, "f").FindProperty("x")!));

        Assert.Equal("f = { id = A y = 2 }", result);
    }

    [Fact]
    public void IndentOfReadsTheLineIndentation()
    {
        Assert.Equal("\t\t", ScriptEdits.IndentOf("a\n\t\tb = 1", 6));
        Assert.Equal("", ScriptEdits.IndentOf("b = 1", 2));
    }

    [Fact]
    public void InsertsMultiLineStatementsWithTheTargetsIndentation()
    {
        const string text = "ideas = {\r\n\tcountry = {\r\n\t\tA = { }\r\n\t}\r\n}\r\n";

        var result = Edit(text, t => ScriptEdits.InsertMultiLine(text, Block(t, "country"), "B = {\n\tremoval_cost = -1\n}"));

        Assert.Equal("ideas = {\r\n\tcountry = {\r\n\t\tA = { }\r\n\t\tB = {\r\n\t\t\tremoval_cost = -1\r\n\t\t}\r\n\t}\r\n}\r\n", result);
    }

    [Fact]
    public void InsertsMultiLineStatementsAtTheEndOfAnEmptyFile()
    {
        Assert.Equal("A = {\n\tx = 1\n}\n", Edit("", t => ScriptEdits.InsertMultiLine("", t.Root, "A = {\n\tx = 1\n}")));
    }
}
