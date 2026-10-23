using System.Globalization;
using System.Text.RegularExpressions;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Content;

namespace HoI4ModBuilder.Game.Tooltips;

/// <summary>What tooltips are built from: the game's texts and what the mod defines.</summary>
public interface ITooltipSource
{
    /// <summary>A localisation text in the chosen language, raw (with colour codes and <c>$placeholders$</c>), or null.</summary>
    string? Text(string key);

    ModifierCatalog Modifiers { get; }

    /// <summary>The <c>modifier</c> block of an idea, when the mod defines it.</summary>
    BlockSyntax? IdeaModifiers(string idea);
}

/// <summary>
/// Effects in words, the way the game's tooltips put them ("Political Power: +100.", "Add Black Soil."),
/// using the game's own texts for the commonly used effects. Hidden parts (flags, variables,
/// <c>hidden_effect</c>) are left out as in the game; anything not known is shown as written.
/// </summary>
public sealed partial class EffectDescriber
{
    // The game shows none of these.
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "hidden_effect", "set_country_flag", "clr_country_flag", "set_global_flag", "clr_global_flag", "set_state_flag",
        "clr_state_flag", "set_character_flag", "clr_character_flag", "set_variable", "add_to_variable", "subtract_from_variable",
        "multiply_variable", "divide_variable", "clear_variable", "set_temp_variable", "add_to_temp_variable",
        "subtract_from_temp_variable", "multiply_temp_variable", "divide_temp_variable", "clamp_variable", "clamp_temp_variable",
        "round_variable", "round_temp_variable", "add_to_array", "remove_from_array", "clear_array", "add_to_temp_array",
        "save_event_target_as", "save_global_event_target_as", "clear_global_event_target", "add_ai_strategy", "log",
        "mark_focus_tree_layout_dirty", "limit", "set_cosmetic_tag_flag", "set_province_name", "ai_chance",
        // An event option's own fields.
        "name", "trigger", "original_recipient_only", "highlight_states",
    };

    private static readonly HashSet<string> Scopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ROOT", "FROM", "PREV", "THIS", "owner", "controller", "capital_scope", "overlord", "faction_leader",
        "FROM.FROM", "PREV.PREV", "ROOT.FROM",
    };

    private readonly ITooltipSource _source;
    private readonly Dictionary<string, Action<PropertySyntax, int, List<TooltipLine>>> _effects;
    private int _fallbacks;

    public EffectDescriber(ITooltipSource source)
    {
        _source = source;
        _effects = new Dictionary<string, Action<PropertySyntax, int, List<TooltipLine>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["add_political_power"] = Amount("POLITICS_ADD_POLITICAL_POWER"),
            ["set_political_power"] = Amount("POLITICS_SET_POLITICAL_POWER"),
            ["add_stability"] = Amount("EFFECT_ADD_STABILITY"),
            ["set_stability"] = Amount("EFFECT_SET_STABILITY"),
            ["add_war_support"] = Amount("EFFECT_ADD_WAR_SUPPORT"),
            ["set_war_support"] = Amount("EFFECT_SET_WAR_SUPPORT"),
            ["add_manpower"] = Amount("EFFECT_MANPOWER_ADD_COUNTRY"),
            ["army_experience"] = Amount("EFFECT_ADD_ARMY_EXPERIENCE"),
            ["navy_experience"] = Amount("EFFECT_ADD_NAVY_EXPERIENCE"),
            ["air_experience"] = Amount("EFFECT_ADD_AIR_EXPERIENCE"),
            ["add_command_power"] = Amount("EFFECT_ADD_COMMAND_POWER"),
            ["add_fuel"] = Amount("POLITICS_ADD_FUEL"),
            ["add_research_slot"] = Amount("EFFECT_ADD_RESEARCH_SLOT", "COUNT"),
            ["add_extra_state_shared_building_slots"] = Amount("EFFECT_ADD_EXTRA_STATE_SHARED_BUILDING_SLOTS", "COUNT"),
            ["add_ideas"] = Ideas("EFFECT_ADD_IDEA", withModifiers: true),
            ["remove_ideas"] = Ideas("EFFECT_REMOVE_IDEA", withModifiers: false),
            ["show_ideas_tooltip"] = Ideas("EFFECT_SHOW_IDEA_TOOLTIP", withModifiers: true),
            ["add_timed_idea"] = TimedIdea,
            ["swap_ideas"] = SwapIdeas,
            ["country_event"] = Event,
            ["news_event"] = Event,
            ["state_event"] = Event,
            ["add_popularity"] = (p, indent, lines) => Template(lines, indent, p, "POLITICS_IDEOLOGY_POPULARITY_CHANGE",
                ("IDEOLOGY", Name(Field(p, "ideology"))), ("VALUE", Number(Field(p, "popularity")))),
            ["set_politics"] = SetPolitics,
            ["add_tech_bonus"] = TechBonus,
            ["add_doctrine_cost_reduction"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_ADD_DOCTRINE_COST_REDUCTION",
                ("USES", Uses(p)), ("REDUCTION", Number(Field(p, "cost_reduction"))), ("LIST", Categories(p))),
            ["create_wargoal"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_CREATE_WARGOAL",
                ("WARGOAL", Name(Field(p, "type"))), ("TARGET", Country(Field(p, "target")))),
            ["declare_war_on"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_DECLARE_WAR",
                ("NAME", "ROOT"), ("COUNTRY", Country(Field(p, "target")))),
            ["transfer_state"] = State("EFFECT_TRANSFER_STATE"),
            ["add_state_claim"] = State("EFFECT_ADD_STATE_CLAIM"),
            ["remove_state_claim"] = State("EFFECT_REMOVE_STATE_CLAIM"),
            ["add_state_core"] = State("EFFECT_ADD_STATE_CORE"),
            ["remove_state_core"] = State("EFFECT_REMOVE_STATE_CORE"),
            ["set_capital"] = State("EFFECT_SET_CAPITAL", "state"),
            ["transfer_state_to"] = Tag("EFFECT_TRANSFER_STATE_TO"),
            ["add_core_of"] = Tag("EFFECT_ADD_CORE_OF"),
            ["remove_core_of"] = Tag("EFFECT_REMOVE_CORE_OF"),
            ["add_claim_by"] = Tag("EFFECT_ADD_CLAIM_BY"),
            ["remove_claim_by"] = Tag("EFFECT_REMOVE_CLAIM_BY"),
            ["add_to_faction"] = Tag("EFFECT_ADD_TO_FACTION"),
            ["remove_from_faction"] = Tag("EFFECT_REMOVE_FROM_FACTION"),
            ["give_guarantee"] = Tag("EFFECT_GIVE_GUARANTEE"),
            ["give_military_access"] = Tag("EFFECT_GIVE_MILITARY_ACCESS"),
            ["release_puppet"] = Tag("EFFECT_RELEASE_PUPPET"),
            ["annex_country"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_ANNEX_COUNTRY",
                ("NAME", "ROOT"), ("TARGET", Country(Field(p, "target")))),
            ["puppet"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_MAKE_PUPPET",
                ("TARGET", Country(Field(p, "target"))), ("COUNTRY", "ROOT")),
            ["add_building_construction"] = Building,
            ["add_resource"] = Resource,
            ["add_equipment_to_stockpile"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_ADD_EQUIPMENT_TO_STOCKPILE",
                ("AMOUNT", Number(Field(p, "amount"))), ("EQUIPMENT", Name(Field(p, "type")))),
            ["add_opinion_modifier"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_ADD_OPINION",
                ("NAME", Name(Field(p, "modifier"))), ("TARGET", Country(Field(p, "target")))),
            ["remove_opinion_modifier"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_REMOVE_OPINION",
                ("NAME", Name(Field(p, "modifier"))), ("TARGET", Country(Field(p, "target")))),
            ["set_cosmetic_tag"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_SET_COSMETIC_TAG",
                ("COUNTRY", "ROOT"), ("COSMETIC_NAME", Name(Field(p, null)))),
            ["load_focus_tree"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_LOAD_FOCUS_TREE",
                ("TREE_NAME", Name(Field(p, "tree")))),
            ["unlock_decision_tooltip"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_UNLOCK_DECISION",
                ("NAME", Name(Field(p, "decision"))), ("EFFECT", "")),
            ["unlock_decision_category_tooltip"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_UNLOCK_DECISION_CATEGORY",
                ("NAME", Name(Field(p, "category")))),
            ["add_country_leader_trait"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_ADD_COUNTRY_LEADER_TRAIT",
                ("LEADER", "ROOT"), ("TRAIT", new[] { new TextRun(Plain(Name(Field(p, null))), 'Y') })),
            ["activate_decision"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_ACTIVATE_DECISION",
                ("NAME", Name(Field(p, null)))),
            ["activate_targeted_decision"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_ACTIVATE_TARGETED_DECISION",
                ("NAME", Name(Field(p, "decision")))),
            ["remove_targeted_decision"] = (p, indent, lines) => Template(lines, indent, p, "EFFECT_REMOVE_TARGETED_DECISION",
                ("NAME", Name(Field(p, "decision")))),
            ["set_technology"] = Technologies,
            ["custom_effect_tooltip"] = (p, indent, lines) =>
            {
                if (Field(p, null) is { } key)
                    lines.Add(new TooltipLine(indent, Trim(GameText.Render(_source.Text(key) ?? key))));
            },
            ["effect_tooltip"] = (p, indent, lines) =>
            {
                if (p.Value is BlockSyntax block)
                    lines.AddRange(Describe(block, indent));
            },
            ["random_list"] = RandomList,
            ["if"] = Conditional("Якщо"),
            ["else_if"] = Conditional("Інакше, якщо"),
            ["else"] = Conditional("Інакше"),
        };
    }

    /// <summary>The lines of an effect block (<c>completion_reward</c>, an event option…).</summary>
    public List<TooltipLine> Describe(IStatementContainer effects, int indent = 0)
    {
        var lines = new List<TooltipLine>();
        foreach (var property in effects.Properties())
            Describe(property, indent, lines);
        return lines;
    }

    /// <summary>An effect worded by the game's own texts; empty when it would only be shown as written.</summary>
    public List<TooltipLine> DescribeKnown(PropertySyntax property)
    {
        var lines = new List<TooltipLine>();
        if (!_effects.TryGetValue(property.Key.ValueText, out var describe))
            return lines;
        _fallbacks = 0;
        describe(property, 0, lines);
        return _fallbacks == 0 ? lines : [];
    }

    /// <summary>The lines of a <c>modifier</c> block: "Stability: +5.00%".</summary>
    public List<TooltipLine> Modifiers(IStatementContainer modifiers, int indent = 0)
    {
        var lines = new List<TooltipLine>();
        foreach (var property in modifiers.Properties())
        {
            if (property.Value is ScalarSyntax scalar
                && _source.Modifiers.Line(property.Key.ValueText, scalar.ValueText, _source.Text, indent) is { } line)
                lines.Add(line);
        }
        return lines;
    }

    private void Describe(PropertySyntax property, int indent, List<TooltipLine> lines)
    {
        var key = property.Key.ValueText;
        if (Hidden.Contains(key))
            return;
        if (_effects.TryGetValue(key, out var describe))
        {
            describe(property, indent, lines);
            return;
        }
        if (property.Value is BlockSyntax block && ScopeTitle(key) is { } title)
        {
            lines.Add(new TooltipLine(indent, [new TextRun(title + ":", 'H')]));
            lines.AddRange(Describe(block, indent + 1));
            return;
        }
        Fallback(property, indent, lines);
    }

    /// <summary>What a scope block is called: "Germany", "Brandenburg", "Every owned state"; null if it isn't one.</summary>
    private string? ScopeTitle(string key)
    {
        if (CountryTag().IsMatch(key))
            return Plain(Country(key));
        if (key.All(char.IsAsciiDigit))
            return Plain(StateName(key));
        if (Scopes.Contains(key))
            return key;
        if (key.StartsWith("every_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("random_", StringComparison.OrdinalIgnoreCase))
            return _source.Text($"EFFECT_{key.ToUpperInvariant()}_STARTS") is { } text ? Plain(GameText.Render(text)) : key;
        return null;
    }

    // ── Effect kinds ───────────────────────────────────────────

    private Action<PropertySyntax, int, List<TooltipLine>> Amount(string template, string name = "VAL") =>
        (p, indent, lines) => Template(lines, indent, p, template, (name, Number(Field(p, null))));

    private Action<PropertySyntax, int, List<TooltipLine>> State(string template, string? field = null) =>
        (p, indent, lines) => Template(lines, indent, p, template, ("STATE", StateName(Field(p, field))));

    private Action<PropertySyntax, int, List<TooltipLine>> Tag(string template) =>
        (p, indent, lines) => Template(lines, indent, p, template, ("COUNTRY", Country(Field(p, "target"))));

    private Action<PropertySyntax, int, List<TooltipLine>> Ideas(string template, bool withModifiers) =>
        (p, indent, lines) =>
        {
            foreach (var idea in Values(p))
            {
                Template(lines, indent, p, template, ("NAME", new[] { new TextRun(Plain(Name(idea)), 'Y') }));
                if (withModifiers)
                    lines.AddRange(IdeaModifiers(idea, indent + 1));
            }
        };

    private void TimedIdea(PropertySyntax p, int indent, List<TooltipLine> lines)
    {
        var idea = Field(p, "idea");
        var days = Number(Field(p, "days"));
        var time = _source.Text("DAYS_TRANSFER") is { } daysText && days is not null
            ? GameText.Render(daysText, new Dictionary<string, object> { ["DAYS"] = days })
            : [new TextRun(Field(p, "days") ?? "")];
        var modifiers = IdeaModifiers(idea, 0);
        var description = modifiers.SelectMany((line, i) => i == 0 ? line.Runs : line.Runs.Prepend(new TextRun(", "))).ToList();
        Template(lines, indent, p, "EFFECT_ADD_TIMED_IDEA", ("IDEA", Name(idea)), ("DESC", description), ("TIME_DESC", time));
    }

    private void SwapIdeas(PropertySyntax p, int indent, List<TooltipLine> lines)
    {
        var added = Field(p, "add_idea");
        Template(lines, indent, p, "EFFECT_SWAP_IDEAS_REPLACE", ("REMOVE", Name(Field(p, "remove_idea"))), ("ADD", Name(added)));
        lines.AddRange(IdeaModifiers(added, indent + 1));
    }

    private void Event(PropertySyntax p, int indent, List<TooltipLine> lines)
    {
        var id = Field(p, "id");
        var title = id is null ? null : _source.Text(id + ".t");
        Template(lines, indent, p, "EFFECT_COUNTRY_EVENT",
            ("EVENT", title is null ? id ?? "" : Plain(GameText.Render(title))));
    }

    private void SetPolitics(PropertySyntax p, int indent, List<TooltipLine> lines)
    {
        Template(lines, indent, p, "EFFECT_SET_POLITICS");
        if (Field(p, "ruling_party") is { } party)
            Template(lines, indent + 1, p, "EFFECT_SET_POLITICS_RULING_PARTY", ("PARTY", Name(party)));
        if (p.Value is BlockSyntax block && ScriptRead.Yes(block, "elections_allowed"))
            Template(lines, indent + 1, p, "EFFECT_SET_POLITICS_ELECTIONS");
    }

    private void TechBonus(PropertySyntax p, int indent, List<TooltipLine> lines)
    {
        var bonus = Number(Field(p, "bonus"));
        if (bonus is null && Number(Field(p, "ahead_reduction")) is { } ahead)
            Template(lines, indent, p, "EFFECT_ADD_TECH_BONUS_AHEAD_ONLY", ("USES", Uses(p)), ("AHEAD", ahead), ("LIST", Categories(p)));
        else
            Template(lines, indent, p, "EFFECT_ADD_TECH_BONUS_BONUS_ONLY", ("USES", Uses(p)), ("BONUS", bonus ?? 0m), ("LIST", Categories(p)));
    }

    private void Technologies(PropertySyntax p, int indent, List<TooltipLine> lines)
    {
        if (p.Value is not BlockSyntax block)
            return;
        foreach (var tech in block.Properties())
            Template(lines, indent, p, "EFFECT_GIVE_TECHNOLOGY", ("NAME", Name(tech.Key.ValueText)));
    }

    private void Building(PropertySyntax p, int indent, List<TooltipLine> lines)
    {
        var instant = p.Value is BlockSyntax block && ScriptRead.Yes(block, "instant_build");
        Template(lines, indent, p, instant ? "EFFECT_ADD_BUILDING_CONSTRUCTION_INSTANT" : "EFFECT_ADD_BUILDING_CONSTRUCTION",
            ("NUMBER", Number(Field(p, "level"))), ("BUILDING", Name(Field(p, "type"))));
    }

    private void Resource(PropertySyntax p, int indent, List<TooltipLine> lines)
    {
        var resource = $"{Field(p, "amount")} {Plain(Name(Field(p, "type")))}".Trim();
        var state = Field(p, "state");
        Template(lines, indent, p, state is null ? "EFFECT_ADD_RESOURCE_NO_STATE" : "EFFECT_ADD_RESOURCE",
            ("RESOURCE", resource), ("STATE", StateName(state)));
    }

    private void RandomList(PropertySyntax p, int indent, List<TooltipLine> lines)
    {
        if (p.Value is not BlockSyntax block)
            return;
        var options = block.Properties()
            .Select(o => (Weight: decimal.TryParse(o.Key.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 0, Option: o))
            .ToList();
        var total = options.Sum(o => o.Weight);
        Template(lines, indent, p, "RANDOM_LIST_EFFECT", ("SIZE", (decimal)options.Count));
        foreach (var (weight, option) in options)
        {
            var share = total > 0 ? weight / total : 0;
            lines.Add(new TooltipLine(indent + 1, [GameText.Number(share, "0%") with { Colour = 'H' }, new TextRun(":")]));
            if (option.Value is BlockSyntax effects)
                lines.AddRange(Describe(effects, indent + 2));
        }
    }

    private Action<PropertySyntax, int, List<TooltipLine>> Conditional(string title) =>
        (p, indent, lines) =>
        {
            if (p.Value is not BlockSyntax block)
                return;
            var runs = new List<TextRun> { new(title, 'H') };
            if (ScriptRead.Blocks(block, "limit").FirstOrDefault() is { } limit)
                runs.Add(new TextRun(" " + Compact(limit), 'g'));
            runs.Add(new TextRun(":", 'H'));
            lines.Add(new TooltipLine(indent, runs));
            lines.AddRange(Describe(block, indent + 1));
        };

    /// <summary>An effect with no known wording: shown as written, a block with its contents below.</summary>
    private void Fallback(PropertySyntax property, int indent, List<TooltipLine> lines)
    {
        _fallbacks++;
        var key = property.Key.ValueText;
        switch (property.Value)
        {
            case ScalarSyntax scalar:
                lines.Add(new TooltipLine(indent, [new TextRun(key + " = "), new TextRun(scalar.ValueText, 'Y')]));
                break;
            // Holding effects (a scope this app doesn't know): what they do, below.
            case BlockSyntax block when block.Properties().Any(IsEffect):
                lines.Add(new TooltipLine(indent, [new TextRun(key + ":")]));
                foreach (var statement in block.Statements.OfType<PropertySyntax>())
                    Describe(statement, indent + 1, lines);
                break;
            // Parameters of an effect: one line, not a line per parameter.
            case BlockSyntax block:
                lines.Add(new TooltipLine(indent, [new TextRun(key + " "), new TextRun(Compact(block, 3), 'Y')]));
                break;
        }
    }

    // ── Helpers ────────────────────────────────────────────────

    /// <summary>Adds a line from a game text, or the effect as written when the text isn't there (no language chosen).</summary>
    private void Template(List<TooltipLine> lines, int indent, PropertySyntax property, string key, params (string Name, object? Value)[] values)
    {
        if (_source.Text(key) is not { } template)
        {
            Fallback(property, indent, lines);
            return;
        }
        var map = values.Where(v => v.Value is not null).ToDictionary(v => v.Name, v => v.Value!);
        lines.Add(new TooltipLine(indent, Trim(GameText.Render(template, map))));
    }

    private IEnumerable<TooltipLine> IdeaModifiers(string? idea, int indent) =>
        idea is not null && _source.IdeaModifiers(idea) is { } modifiers ? Modifiers(modifiers, indent) : [];

    /// <summary>The value of <c>effect = value</c>, or of <paramref name="field"/> in <c>effect = { field = value }</c>.</summary>
    private static string? Field(PropertySyntax property, string? field) => property.Value switch
    {
        ScalarSyntax scalar => scalar.ValueText,
        BlockSyntax block when field is not null => ScriptRead.Scalar(block, field),
        _ => null,
    };

    /// <summary>The values of <c>key = value</c> or <c>key = { a b c }</c>.</summary>
    private static IEnumerable<string> Values(PropertySyntax property) => property.Value switch
    {
        ScalarSyntax scalar => [scalar.ValueText],
        BlockSyntax block => block.Statements.OfType<ElementSyntax>().Select(e => e.Value).OfType<ScalarSyntax>().Select(s => s.ValueText),
        _ => [],
    };

    private static decimal? Number(string? value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    private IReadOnlyList<TextRun> Name(string? key) =>
        key is null ? [] : _source.Text(key) is { } text ? GameText.Render(text) : [new TextRun(key)];

    private IReadOnlyList<TextRun> Country(string? tag) => Name(tag);

    private IReadOnlyList<TextRun> StateName(string? id) =>
        id is null ? [] : _source.Text("STATE_" + id) is { } text ? GameText.Render(text) : [new TextRun(id)];

    private IReadOnlyList<TextRun> Uses(PropertySyntax p) =>
        Field(p, "uses") is { } uses && _source.Text("EFFECT_TECH_BONUS_USES") is { } template
            ? GameText.Render(template, new Dictionary<string, object> { ["USES"] = uses })
            : [];

    private IReadOnlyList<TextRun> Categories(PropertySyntax p)
    {
        if (p.Value is not BlockSyntax block)
            return [];
        var names = block.Properties()
            .Where(f => f.Key.ValueText is "category" or "technology")
            .Select(f => f.Value is ScalarSyntax s ? Plain(Name(s.ValueText)) : "")
            .Where(n => n.Length > 0);
        return [new TextRun(string.Join(", ", names))];
    }

    private static string Plain(IEnumerable<TextRun> runs) => GameText.Plain(runs);

    /// <summary>Drops the trailing line breaks and spaces some game texts end with.</summary>
    private static IReadOnlyList<TextRun> Trim(IReadOnlyList<TextRun> runs)
    {
        if (runs.Count == 0)
            return runs;
        var list = runs.ToList();
        list[^1] = list[^1] with { Text = list[^1].Text.TrimEnd() };
        return list;
    }

    private bool IsEffect(PropertySyntax property) =>
        _effects.ContainsKey(property.Key.ValueText) || property.Value is BlockSyntax && ScopeTitle(property.Key.ValueText) is not null;

    /// <summary>A block on one line: <c>has_war = yes, tag = GER</c>; past <paramref name="limit"/> statements, "…".</summary>
    private static string Compact(BlockSyntax block, int limit = int.MaxValue)
    {
        var parts = block.Statements.Take(limit).Select(s => s switch
        {
            PropertySyntax { Value: ScalarSyntax value } p => $"{p.Key.ValueText} {p.Operator?.Text ?? "="} {value.ValueText}",
            PropertySyntax p => $"{p.Key.ValueText} = {{…}}",
            ElementSyntax { Value: ScalarSyntax value } => value.ValueText,
            _ => "…",
        });
        return string.Join(", ", block.Statements.Length > limit ? parts.Append("…") : parts);
    }

    [GeneratedRegex("^[A-Z][A-Z0-9]{2}$")]
    private static partial Regex CountryTag();
}
