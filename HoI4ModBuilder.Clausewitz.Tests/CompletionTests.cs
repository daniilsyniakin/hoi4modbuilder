using HoI4ModBuilder.Clausewitz.Completion;

namespace HoI4ModBuilder.Clausewitz.Tests;

public class CompletionTests
{
    /// <summary>Parses text with a <c>|</c> caret marker and returns the context at the caret.</summary>
    private static (CompletionContext Context, SyntaxTree Tree) At(string textWithCaret)
    {
        var offset = textWithCaret.IndexOf('|');
        var tree = SyntaxTree.Parse(textWithCaret.Remove(offset, 1));
        return (ScriptCompletion.GetContext(tree, offset), tree);
    }

    [Fact]
    public void KeyAtTopLevel()
    {
        var (context, _) = At("foc|");
        Assert.Equal(CompletionPosition.Key, context.Position);
        Assert.Equal("foc", context.Prefix);
        Assert.Equal(0, context.ReplaceStart);
        Assert.Empty(context.Path);
    }

    [Fact]
    public void KeyInsideNestedBlock()
    {
        var (context, _) = At("focus_tree = {\n\tfocus = {\n\t\tid = x\n\t\tco|\n\t}\n}");
        Assert.Equal(CompletionPosition.Key, context.Position);
        Assert.Equal("co", context.Prefix);
        Assert.Equal(["focus_tree", "focus"], context.Path);
    }

    [Fact]
    public void KeyInUnclosedBlock()
    {
        var (context, _) = At("focus = {\n\tid = x\n\t|");
        Assert.Equal(CompletionPosition.Key, context.Position);
        Assert.Equal("", context.Prefix);
        Assert.Equal(["focus"], context.Path);
    }

    [Fact]
    public void ValueAfterOperator()
    {
        var (context, _) = At("limit = { has_war = y| }");
        Assert.Equal(CompletionPosition.Value, context.Position);
        Assert.Equal("has_war", context.Key);
        Assert.Equal("y", context.Prefix);
        Assert.Equal(["limit"], context.Path);
    }

    [Fact]
    public void ValueWithNothingTypedYet()
    {
        var (context, _) = At("has_war = |");
        Assert.Equal(CompletionPosition.Value, context.Position);
        Assert.Equal("has_war", context.Key);
        Assert.Equal("", context.Prefix);
    }

    [Fact]
    public void AfterValueComesANewKey()
    {
        var (context, _) = At("a = b\nc|");
        Assert.Equal(CompletionPosition.Key, context.Position);
    }

    [Fact]
    public void EditingKeyThatAlreadyHasOperator()
    {
        var (context, _) = At("co| = 10");
        Assert.Equal(CompletionPosition.Key, context.Position);
        Assert.True(context.OperatorFollows);
    }

    [Theory]
    [InlineData("a = b # comm|ent")]
    [InlineData("# comment|")]
    [InlineData("a = \"str|ing\"")]
    public void NothingInCommentsAndStrings(string text)
    {
        Assert.Equal(CompletionPosition.None, At(text).Context.Position);
    }

    [Fact]
    public void VocabularyLearnsKeysPerContextAndValuesPerKey()
    {
        var vocabulary = new ScriptVocabulary();
        vocabulary.AddFile(SyntaxTree.Parse("""
            focus_tree = {
                focus = { id = A cost = 10 available = { has_war = yes } }
                focus = { id = B cost = 5 available = { has_war = no } }
            }
            """), "common/national_focus");

        var root = vocabulary.GetKeys(ScriptVocabulary.RootContext("common/national_focus"))!;
        Assert.True(root["focus_tree"].IsMostlyBlock);

        var focusKeys = vocabulary.GetKeys("focus")!;
        Assert.Equal(["available", "cost", "id"], focusKeys.Keys.Order());
        Assert.Equal(2, focusKeys["cost"].Count);
        Assert.Empty(focusKeys["cost"].Values); // numbers aren't worth suggesting

        Assert.Equal(["no", "yes"], vocabulary.GetKeys("available")!["has_war"].Values.Keys.Order());
    }

    [Fact]
    public void ItemsForKeyComeFromTheEnclosingBlock()
    {
        var vocabulary = new ScriptVocabulary();
        vocabulary.AddFile(SyntaxTree.Parse("focus = { id = A cost = 10 completion_reward = { } }"), "common/national_focus");

        var (context, tree) = At("focus = { c| }");
        var items = ScriptCompletion.GetItems(context, vocabulary, "common/national_focus", tree);

        Assert.Equal(["completion_reward", "cost"], items.Where(i => i.Kind == CompletionItemKind.Key).Select(i => i.Label).Order());
        Assert.True(items.Single(i => i.Label == "completion_reward").InsertsBlock);
        Assert.False(items.Single(i => i.Label == "cost").InsertsBlock);
    }

    [Fact]
    public void ItemsForValueComeFromTheKey()
    {
        var vocabulary = new ScriptVocabulary();
        vocabulary.AddFile(SyntaxTree.Parse("limit = { has_war = yes } limit = { has_war = no }"), "events");

        var (context, tree) = At("limit = { has_war = | }");
        var items = ScriptCompletion.GetItems(context, vocabulary, "events", tree);

        Assert.Equal(["no", "yes"], items.Where(i => i.Kind == CompletionItemKind.Value).Select(i => i.Label).Order());
    }

    [Fact]
    public void DocumentWordsAreOfferedButNotTheWordBeingTyped()
    {
        var (context, tree) = At("focus = { id = UKR_industry }\nprerequisite = { focus = UKR_i| }");
        var items = ScriptCompletion.GetItems(context, null, "common/national_focus", tree);

        Assert.Contains(items, i => i.Label == "UKR_industry" && i.Kind == CompletionItemKind.Word);
        Assert.DoesNotContain(items, i => i.Label == "UKR_i");
    }
}
