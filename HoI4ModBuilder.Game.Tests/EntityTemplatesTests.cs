using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Content;

namespace HoI4ModBuilder.Game.Tests;

public sealed class EntityTemplatesTests
{
    private static string Add(ContentKind kind, string text, string id, string? group = null) =>
        ScriptEdits.Apply(text, EntityTemplates.Add(kind, text, id, group));

    /// <summary>The result parses cleanly and the content parser finds the new entity in it.</summary>
    private static void AssertDefines(string text, string relativePath, ContentKind kind, string id)
    {
        var tree = SyntaxTree.Parse(text);
        Assert.False(tree.HasErrors, text);
        Assert.NotNull(ContentParser.Locate(tree, relativePath, kind, id));
    }

    [Theory]
    [InlineData(ContentKind.FocusTree, "common/national_focus/new.txt", "ukr_focus")]
    [InlineData(ContentKind.Event, "events/new.txt", "ukr.1")]
    [InlineData(ContentKind.Idea, "common/ideas/new.txt", "UKR_spirit")]
    [InlineData(ContentKind.Decision, "common/decisions/new.txt", "UKR_decision")]
    [InlineData(ContentKind.Technology, "common/technologies/new.txt", "ukr_tech")]
    [InlineData(ContentKind.Character, "common/characters/new.txt", "UKR_hero")]
    [InlineData(ContentKind.Country, "common/country_tags/new.txt", "UKR")]
    [InlineData(ContentKind.ScriptedEffect, "common/scripted_effects/new.txt", "ukr_effect")]
    [InlineData(ContentKind.ScriptedTrigger, "common/scripted_triggers/new.txt", "ukr_trigger")]
    public void NewFilesDefineTheEntity(ContentKind kind, string path, string id)
    {
        var text = EntityTemplates.NewFile(kind, id);

        AssertDefines(text, path, kind, id);
        Assert.Equal(path[..path.LastIndexOf('/')], EntityTemplates.Folder(kind));
    }

    [Fact]
    public void NewEventFileDeclaresTheNamespaceFirst()
    {
        var text = EntityTemplates.NewFile(ContentKind.Event, "ukr.1");

        Assert.StartsWith("add_namespace = ukr\n\ncountry_event = {\n\tid = ukr.1", text);
    }

    [Fact]
    public void AddsIdeaIntoTheExistingSlotWithItsIndentation()
    {
        const string text = "ideas = {\n\tcountry = {\n\t\tUKR_a = {\n\t\t\tremoval_cost = -1\n\t\t}\n\t}\n}\n";

        var result = Add(ContentKind.Idea, text, "UKR_b", "country");

        AssertDefines(result, "common/ideas/a.txt", ContentKind.Idea, "UKR_b");
        Assert.Contains("\n\t\tUKR_b = {\n\t\t\tallowed = {\n\t\t\t\talways = no\n\t\t\t}", result);
        Assert.Single(SyntaxTree.Parse(result).Root.Properties("ideas"));
    }

    [Fact]
    public void CreatesMissingSlotInsideIdeas()
    {
        const string text = "ideas = {\n\tcountry = {\n\t\tUKR_a = { }\n\t}\n}\n";

        var result = Add(ContentKind.Idea, text, "UKR_adv", "political_advisor");

        AssertDefines(result, "common/ideas/a.txt", ContentKind.Idea, "UKR_adv");
        Assert.Contains("\n\tpolitical_advisor = {\n\t\tUKR_adv = {", result);
    }

    [Fact]
    public void AddsDecisionToItsCategory()
    {
        const string text = "UKR_reforms = {\n\tUKR_a = { }\n}\n";

        var result = Add(ContentKind.Decision, text, "UKR_b", "UKR_reforms");

        var decision = ContentParser.Parse(SyntaxTree.Parse(result), "common/decisions/a.txt").Single(e => e.Id == "UKR_b");
        Assert.Equal("UKR_reforms", decision.Group);
    }

    [Fact]
    public void AddsEventAndDeclaresANewNamespace()
    {
        const string text = "add_namespace = ukr\n\ncountry_event = { id = ukr.1 }\n";

        var sameNamespace = Add(ContentKind.Event, text, "ukr.2");
        var newNamespace = Add(ContentKind.Event, text, "pol.1");

        AssertDefines(sameNamespace, "events/a.txt", ContentKind.Event, "ukr.2");
        Assert.Single(SyntaxTree.Parse(sameNamespace).Root.Properties("add_namespace"));
        AssertDefines(newNamespace, "events/a.txt", ContentKind.Event, "pol.1");
        Assert.StartsWith("add_namespace = pol\n", newNamespace);
    }

    [Fact]
    public void KeepsWindowsLineBreaks()
    {
        const string text = "characters = {\r\n\tUKR_a = { }\r\n}\r\n";

        var result = Add(ContentKind.Character, text, "UKR_b");

        Assert.DoesNotContain("\n\t\tUKR_b", result.Replace("\r\n", "\r"));
        Assert.Contains("\r\n\tUKR_b = {\r\n\t\tname = UKR_b\r\n", result);
    }
}
