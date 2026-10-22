using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Game.Content;

namespace HoI4ModBuilder.Game.Tests;

public sealed class ContentParserTests
{
    private static IReadOnlyList<ContentEntry> Parse(string relativePath, string text) =>
        ContentParser.Parse(SyntaxTree.Parse(text), relativePath);

    [Fact]
    public void FocusTreeListsItsFocuses()
    {
        const string text = """
            focus_tree = {
                id = ukraine_focus
                country = { factor = 0 }
                focus = { id = UKR_start x = 1 }
                focus = { id = UKR_army }
            }
            shared_focus = { id = SHARED_one }
            """;

        var entries = Parse("common/national_focus/ukraine.txt", text);

        Assert.Equal(2, entries.Count);
        var tree = entries[0];
        Assert.Equal((ContentKind.FocusTree, "ukraine_focus"), (tree.Kind, tree.Id));
        Assert.Equal(["UKR_start", "UKR_army"], tree.Children.Select(c => c.Id));
        Assert.Equal(text.IndexOf("focus = { id = UKR_army", StringComparison.Ordinal), tree.Children[1].Position);
        Assert.Equal((ContentKind.Focus, "SHARED_one"), (entries[1].Kind, entries[1].Id));
    }

    [Fact]
    public void EventsAreGroupedByNamespace()
    {
        const string text = """
            add_namespace = ukr
            country_event = { id = ukr.1 }
            news_event = { id = ukr_news.2 }
            state_event = { }
            """;

        var entries = Parse(@"events\Ukraine.txt", text);

        Assert.Equal([("ukr.1", "ukr"), ("ukr_news.2", "ukr_news")], entries.Select(e => (e.Id, e.Group!)));
    }

    [Fact]
    public void IdeasAreGroupedBySlot()
    {
        const string text = """
            ideas = {
                country = {
                    UKR_spirit = { modifier = { stability_factor = 0.1 } }
                }
                political_advisor = {
                    use_list_view = yes
                    UKR_advisor = { }
                }
            }
            """;

        var entries = Parse("common/ideas/ukraine.txt", text);

        Assert.Equal([("UKR_spirit", "country"), ("UKR_advisor", "political_advisor")], entries.Select(e => (e.Id, e.Group!)));
    }

    [Fact]
    public void DecisionsAreGroupedByCategory()
    {
        var entries = Parse("common/decisions/ukraine.txt", "UKR_reforms = { UKR_land = { cost = 50 } UKR_sea = { } }");

        Assert.Equal(["UKR_land", "UKR_sea"], entries.Select(e => e.Id));
        Assert.All(entries, e => Assert.Equal("UKR_reforms", e.Group));
    }

    [Fact]
    public void DecisionCategoriesAreNotDecisions()
    {
        Assert.False(ContentParser.IsContentFile("common/decisions/categories/ukraine.txt"));
        Assert.Empty(Parse("common/decisions/categories/ukraine.txt", "UKR_reforms = { icon = x }"));
    }

    [Fact]
    public void CountryTagsSkipSettings()
    {
        var entries = Parse("common/country_tags/00_countries.txt", """
            dynamic_tags = yes
            UKR = "countries/Ukraine.txt"
            """);

        var country = Assert.Single(entries);
        Assert.Equal(("UKR", "countries/Ukraine.txt"), (country.Id, country.Detail));
    }

    [Fact]
    public void StatesUseTheirNumericId()
    {
        var state = Assert.Single(Parse("history/states/202-Kiev.txt", "state = { id = 202 name = \"STATE_202\" }"));

        Assert.Equal(("202", "STATE_202"), (state.Id, state.Detail));
    }

    [Fact]
    public void ScriptedEffectsSkipVariables()
    {
        var entries = Parse("common/scripted_effects/ukr.txt", "@cost = 5\nukr_effect = { add_political_power = @cost }");

        Assert.Equal(["ukr_effect"], entries.Select(e => e.Id));
    }

    [Fact]
    public void TechnologiesAreGroupedByFile()
    {
        var entries = Parse("common/technologies/infantry.txt", "technologies = { @x = 1 infantry_weapons = { } }");

        var tech = Assert.Single(entries);
        Assert.Equal(("infantry_weapons", "infantry"), (tech.Id, tech.Group));
    }

    [Fact]
    public void OtherFilesHaveNoContent()
    {
        Assert.False(ContentParser.IsContentFile("common/national_focus/sub/tree.txt"));
        Assert.False(ContentParser.IsContentFile("events/readme.md"));
        Assert.True(ContentParser.IsContentFile(@"common\ideas\x.txt"));
        Assert.Empty(Parse("interface/x.txt", "ideas = { country = { a = { } } }"));
    }
}
