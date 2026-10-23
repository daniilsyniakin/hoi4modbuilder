using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Game.Content;

namespace HoI4ModBuilder.Game.Tests;

public sealed class UsagesTests
{
    [Fact]
    public void FindsUsesWithTheirOwnerAndPlace()
    {
        var tree = SyntaxTree.Parse("""
            focus_tree = {
                id = ukraine_focus
                focus = {
                    id = UKR_industry
                    completion_reward = { add_ideas = UKR_black_soil }
                }
                focus = {
                    id = UKR_tanks
                    prerequisite = { focus = UKR_industry }
                }
            }
            """);

        var uses = Usages.InScript(tree, "common/national_focus/ukraine.txt", "UKR_industry").ToList();

        Assert.Equal(2, uses.Count);
        Assert.True(uses[0].IsDefinition);
        Assert.Equal(("UKR_tanks", "prerequisite › focus", 9, false), (uses[1].Owner?.Id, uses[1].Context, uses[1].Line, uses[1].IsDefinition));
        Assert.Equal("prerequisite = { focus = UKR_industry }", uses[1].Snippet);

        var idea = Usages.InScript(tree, "common/national_focus/ukraine.txt", "UKR_black_soil").Single();
        Assert.Equal(("UKR_industry", "completion_reward › add_ideas"), (idea.Owner?.Id, idea.Context));
    }

    [Fact]
    public void AnIdeaIsDefinedByItsKey()
    {
        var tree = SyntaxTree.Parse("ideas = { country = { UKR_black_soil = { modifier = { stability_factor = 0.05 } } } }");

        var use = Usages.InScript(tree, "common/ideas/ukraine.txt", "UKR_black_soil").Single();

        Assert.True(use.IsDefinition);
    }

    [Fact]
    public void FindsLocalisationKeys()
    {
        var keys = Usages.InLocalisation("l_english:\n UKR_industry:0 \"Industrial Push\"\n UKR_industry_desc:0 \"…\"\n UKR_industrial:0 \"x\"\n", "UKR_industry")
            .Select(u => u.Context).ToList();

        Assert.Equal(["UKR_industry", "UKR_industry_desc"], keys);
    }
}
