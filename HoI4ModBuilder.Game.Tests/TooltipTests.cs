using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Tooltips;

namespace HoI4ModBuilder.Game.Tests;

public sealed class TooltipTests
{
    // Texts as the game ships them (localisation/english).
    private static readonly Dictionary<string, string> Texts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["POLITICS_ADD_POLITICAL_POWER"] = "Political Power: $VAL|=+0$.",
        ["EFFECT_ADD_STABILITY"] = "Gain Base Stability: $VAL|+=%$.",
        ["EFFECT_ADD_IDEA"] = "Add $NAME$.",
        ["EFFECT_COUNTRY_EVENT"] = "Gets event '$EVENT|Y$'.\\n",
        ["EFFECT_ADD_STATE_CORE"] = "Gains core on $STATE|Y$.",
        ["EFFECT_ANNEX_COUNTRY"] = "§Y$NAME|U$§! annexes §Y$TARGET$§!.",
        ["EFFECT_EVERY_OWNED_STATE_STARTS"] = "Every owned state",
        ["MODIFIER_STABILITY_FACTOR"] = "Stability",
        ["MODIFIER_CONSUMER_GOODS_FACTOR"] = "Consumer Goods Factories",
        ["MODIFIER_POLITICAL_POWER_GAIN"] = "Daily Political Power Gain",
        ["UKR_black_soil"] = "Black Soil",
        ["ukr.1.t"] = "A New Dawn",
        ["STATE_202"] = "Kiev",
        ["GER"] = "Germany",
    };

    private sealed class Source : ITooltipSource
    {
        public string? Text(string key) => Texts.GetValueOrDefault(key);

        public ModifierCatalog Modifiers { get; } = ModifierCatalog.Empty;

        public BlockSyntax? IdeaModifiers(string idea) =>
            idea == "UKR_black_soil" ? (BlockSyntax)SyntaxTree.Parse("m = { stability_factor = 0.05 consumer_goods_factor = 0.1 }").Root.Properties().First().Value : null;
    }

    private static List<string> Describe(string effects) =>
        new EffectDescriber(new Source()).Describe(SyntaxTree.Parse(effects).Root).Select(l => l.ToString()).ToList();

    [Fact]
    public void FormatsNumbersLikeTheGame()
    {
        Assert.Equal(new TextRun("+5%", 'G'), GameText.Number(0.05m, "+=%"));
        Assert.Equal(new TextRun("-10%", 'R'), GameText.Number(-0.1m, "+=%"));
        Assert.Equal(new TextRun("+100", null), GameText.Number(100m, "=+0") with { Colour = null });
        Assert.Equal("0.5", GameText.Number(0.5m).Text);
        Assert.Equal(new TextRun("12", 'Y'), GameText.Number(12m, "Y"));
    }

    [Fact]
    public void RendersColoursPlaceholdersAndIcons()
    {
        var runs = GameText.Render("§Y$NAME|U$§! annexes £pol_power §Y$TARGET$§! @$TAG$$TARGET$!",
            new Dictionary<string, object> { ["NAME"] = "germany", ["TARGET"] = "Austria" });

        Assert.Equal([new TextRun("Germany", 'Y'), new TextRun(" annexes  "), new TextRun("Austria", 'Y'), new TextRun(" Austria!")], runs);
    }

    [Fact]
    public void DescribesCommonEffectsWithTheGamesTexts()
    {
        var lines = Describe("""
            add_political_power = 100
            add_stability = -0.05
            add_ideas = UKR_black_soil
            country_event = { id = ukr.1 days = 3 }
            set_country_flag = secret
            hidden_effect = { add_political_power = 1000 }
            202 = { add_core_of = GER }
            every_owned_state = { limit = { is_core_of = ROOT } add_state_core = THIS }
            if = { limit = { has_war = yes } add_stability = 0.1 }
            unknown_effect = 5
            """);

        Assert.Equal(
        [
            "Political Power: +100.",
            "Gain Base Stability: -5%.",
            "Add Black Soil.",
            "  Stability: +5.0%",
            "  Consumer Goods Factories: +10.0%",
            "Gets event 'A New Dawn'.",
            "Kiev:",
            "  add_core_of = GER",
            "Every owned state:",
            "  Gains core on THIS.",
            "Якщо has_war = yes:",
            "  Gain Base Stability: +10%.",
            "unknown_effect = 5",
        ], lines);
    }

    [Fact]
    public void ColoursModifiersByWhetherMoreIsGood()
    {
        var catalog = ModifierCatalog.Empty;
        Assert.Equal(new TextRun("+5.0%", 'G'), catalog.Value("stability_factor", "0.05"));
        Assert.Equal(new TextRun("+10.0%", 'R'), catalog.Value("consumer_goods_factor", "0.1"));
        Assert.Equal(new TextRun("-0.10", 'R'), catalog.Value("political_power_gain", "-0.1"));
        Assert.Equal("Daily Political Power Gain: +0.50", catalog.Line("political_power_gain", "0.5", Texts.GetValueOrDefault)!.PlainText);
    }
}
