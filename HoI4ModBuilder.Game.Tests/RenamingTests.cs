using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Game.Content;

namespace HoI4ModBuilder.Game.Tests;

public sealed class RenamingTests
{
    private const string Focuses = """
        focus_tree = {
            id = tree
            focus = { id = UKR_army x = 1 }
            focus = { id = UKR_army_reform prerequisite = { focus = UKR_army } relative_position_id = "UKR_army" }
        }
        """;

    [Fact]
    public void RenamesOnlyTheDefinition()
    {
        var tree = SyntaxTree.Parse(Focuses);

        var edit = Renaming.RenameDefinition(tree, "common/national_focus/a.txt", ContentKind.Focus, "UKR_army", "UKR_military")!.Value;
        var result = ScriptEdits.Apply(Focuses, [edit]);

        Assert.Contains("focus = { id = UKR_military x = 1 }", result);
        Assert.Contains("prerequisite = { focus = UKR_army }", result);
    }

    [Fact]
    public void RenamesWholeTokensEverywhereKeepingQuotes()
    {
        var result = ScriptEdits.Apply(Focuses, Renaming.RenameReferences(SyntaxTree.Parse(Focuses), "UKR_army", "UKR_military"));

        Assert.Contains("id = UKR_military x = 1", result);
        Assert.Contains("id = UKR_army_reform", result);
        Assert.Contains("focus = UKR_military", result);
        Assert.Contains("relative_position_id = \"UKR_military\"", result);
    }

    [Fact]
    public void RenamesKeyDefinedEntities()
    {
        const string text = "ideas = { country = { UKR_soil = { removal_cost = -1 } } }";

        var edit = Renaming.RenameDefinition(SyntaxTree.Parse(text), "common/ideas/a.txt", ContentKind.Idea, "UKR_soil", "UKR_land")!.Value;

        Assert.Equal("ideas = { country = { UKR_land = { removal_cost = -1 } } }", ScriptEdits.Apply(text, [edit]));
    }

    [Fact]
    public void RenamesLocalisationKeys()
    {
        const string text = "l_english:\n UKR_army:0 \"Army\"\n UKR_army_desc:0 \"Desc\"\n UKR_army_reform:0 \"Other\"\n";

        var result = ScriptEdits.Apply(text, Renaming.RenameLocalisationKeys(text, "UKR_army", "UKR_mil"));

        Assert.Equal("l_english:\n UKR_mil:0 \"Army\"\n UKR_mil_desc:0 \"Desc\"\n UKR_army_reform:0 \"Other\"\n", result);
    }

    [Fact]
    public void ValidatesIds()
    {
        Assert.True(Renaming.IsValidId("UKR_new.1"));
        Assert.False(Renaming.IsValidId("two words"));
        Assert.False(Renaming.IsValidId(""));
    }
}
