using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Content;

namespace HoI4ModBuilder.Game.Tests;

public sealed class FocusAndEventTests
{
    private const string FocusFile = """
        focus_tree = {
            id = ukr
            shared_focus = SH_root
            focus = {
                id = A
                icon = GFX_goal_a
                x = 5 y = 0
                cost = 10
            }
            focus = {
                id = B
                icon = { trigger = { always = yes } value = GFX_goal_b }
                relative_position_id = A
                x = -1 y = 1
                prerequisite = { focus = A }
                mutually_exclusive = { focus = C }
                completion_reward = { country_event = ukr.1 }
            }
            focus = {
                id = C
                relative_position_id = A
                x = 1 y = 1
                prerequisite = { focus = A focus = B }
                prerequisite = { focus = A }
                completion_reward = { country_event = { id = ukr.2 days = 3 } }
            }
        }
        shared_focus = { id = SH_root x = 10 y = 0 }
        shared_focus = { id = SH_child relative_position_id = SH_root y = 1 prerequisite = { focus = SH_root } }
        shared_focus = { id = SH_other x = 20 }
        """;

    private const string EventFile = """
        add_namespace = ukr
        country_event = {
            id = ukr.1
            title = ukr.1.t
            desc = { trigger = { always = yes } text = ukr.1.d }
            picture = GFX_report_event_generic
            is_triggered_only = yes
            immediate = { news_event = ukr_news.1 }
            option = {
                name = ukr.1.a
                ai_chance = { factor = 10 }
                add_political_power = 100
                add_ideas = { UKR_spirit }
                country_event = ukr.2
            }
            option = {
                name = ukr.1.b
                random_events = { 50 = ukr.3 50 = 0 }
            }
        }
        """;

    private static BlockSyntax TreeBlock(SyntaxTree tree) =>
        (BlockSyntax)tree.Root.FindProperty("focus_tree")!.Value;

    [Fact]
    public void ReadsFocusesWithConditionalIconsAndPrerequisites()
    {
        var focuses = FocusTrees.ReadTree(TreeBlock(SyntaxTree.Parse(FocusFile))).ToList();

        Assert.Equal(["A", "B", "C"], focuses.Select(f => f.Id));
        Assert.Equal(("GFX_goal_a", "10"), (focuses[0].Icon, focuses[0].Cost));
        Assert.Equal("GFX_goal_b", focuses[1].Icon);
        Assert.Equal(["C"], focuses[1].MutuallyExclusive);
        Assert.Equal(2, focuses[2].Prerequisites.Count);
        Assert.Equal(["A", "B"], focuses[2].Prerequisites[0]);
    }

    [Fact]
    public void LaysOutRelativePositions()
    {
        var focuses = FocusTrees.ReadTree(TreeBlock(SyntaxTree.Parse(FocusFile))).ToList();

        var layout = FocusTrees.Layout(focuses);

        Assert.Equal((5, 0), layout["A"]);
        Assert.Equal((4, 1), layout["B"]);
        Assert.Equal((6, 1), layout["C"]);
    }

    [Fact]
    public void CircularRelativePositionsDoNotHang()
    {
        var tree = SyntaxTree.Parse("t = { focus = { id = A relative_position_id = B x = 1 } focus = { id = B relative_position_id = A x = 2 } }");
        var focuses = FocusTrees.ReadTree((BlockSyntax)tree.Root.FindProperty("t")!.Value).ToList();

        var layout = FocusTrees.Layout(focuses);

        Assert.Equal(2, layout.Count);
    }

    [Fact]
    public void CollectsSharedBranches()
    {
        var tree = SyntaxTree.Parse(FocusFile);
        var shared = tree.Root.Properties("shared_focus").Select(FocusTrees.Read).OfType<FocusDefinition>().ToList();

        var included = FocusTrees.CollectShared(FocusTrees.SharedReferences(TreeBlock(tree)), shared);

        Assert.Equal(["SH_root", "SH_child"], included.Select(f => f.Id));
        Assert.Equal((10, 1), FocusTrees.Layout(included)["SH_child"]);
    }

    [Fact]
    public void ReadsEventWithOptionsAndFiredEvents()
    {
        var tree = SyntaxTree.Parse(EventFile);
        var ev = Events.Read(tree.Root.FindProperty("country_event")!)!;

        Assert.Equal(("ukr.1", "ukr.1.t", "ukr.1.d"), (ev.Id, ev.Title, ev.Description));
        Assert.True(ev.IsTriggeredOnly);
        Assert.Equal(["ukr_news.1"], ev.ImmediateEvents);
        Assert.Equal(2, ev.Options.Count);
        Assert.Equal("ukr.1.a", ev.Options[0].Name);
        Assert.Equal(["add_political_power = 100", "add_ideas = { … }"], ev.Options[0].Effects);
        Assert.Equal(["ukr.2"], ev.Options[0].FiredEvents);
        Assert.Equal(["ukr.3"], ev.Options[1].FiredEvents);
    }

    [Fact]
    public void FindsWhatFiresEvents()
    {
        var focusTriggers = Events.FindTriggers(SyntaxTree.Parse(FocusFile), "common/national_focus/ukr.txt").ToList();
        var eventTriggers = Events.FindTriggers(SyntaxTree.Parse(EventFile), "events/ukr.txt").ToList();

        Assert.Equal([("B", "ukr.1"), ("C", "ukr.2")], focusTriggers.Select(t => (t.SourceId, t.EventId)));
        Assert.All(focusTriggers, t => Assert.Equal(ContentKind.Focus, t.SourceKind));
        Assert.Equal(
            [("immediate", null, "ukr_news.1"), ("ukr.1.a", 0, "ukr.2"), ("ukr.1.b", 1, "ukr.3")],
            eventTriggers.Select(t => (t.Detail, t.OptionIndex, t.EventId)));
    }

    [Fact]
    public void LocatesEntitiesInAFreshTree()
    {
        var tree = SyntaxTree.Parse(FocusFile);

        var focus = ContentParser.Locate(tree, "common/national_focus/ukr.txt", ContentKind.Focus, "C");

        Assert.NotNull(focus);
        Assert.Equal("C", ScriptRead.Scalar((BlockSyntax)focus.Value, "id"));
        Assert.Null(ContentParser.Locate(tree, "common/national_focus/ukr.txt", ContentKind.Focus, "missing"));
    }
}
