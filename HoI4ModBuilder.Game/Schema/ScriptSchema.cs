using System.Text.RegularExpressions;
using HoI4ModBuilder.Clausewitz.Editing;

namespace HoI4ModBuilder.Game.Schema;

/// <summary>
/// The commands the game understands (from its documentation) and the rules for where they go:
/// which blocks hold effects, which hold triggers, how scopes change.
/// </summary>
public sealed partial class ScriptSchema
{
    // Entity properties that hold a kind of script.
    private static readonly Dictionary<string, ScriptContext> EntityBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["completion_reward"] = ScriptContext.Effect,
        ["select_effect"] = ScriptContext.Effect,
        ["immediate"] = ScriptContext.Effect,
        ["option"] = ScriptContext.Effect,
        ["complete_effect"] = ScriptContext.Effect,
        ["remove_effect"] = ScriptContext.Effect,
        ["timeout_effect"] = ScriptContext.Effect,
        ["cancel_effect"] = ScriptContext.Effect,
        ["on_add"] = ScriptContext.Effect,
        ["on_remove"] = ScriptContext.Effect,
        ["effect"] = ScriptContext.Effect,
        ["available"] = ScriptContext.Trigger,
        ["bypass"] = ScriptContext.Trigger,
        ["allow_branch"] = ScriptContext.Trigger,
        ["trigger"] = ScriptContext.Trigger,
        ["allowed"] = ScriptContext.Trigger,
        ["allowed_civil_war"] = ScriptContext.Trigger,
        ["visible"] = ScriptContext.Trigger,
        ["cancel"] = ScriptContext.Trigger,
        ["target_trigger"] = ScriptContext.Trigger,
        ["cancel_trigger"] = ScriptContext.Trigger,
        ["remove_trigger"] = ScriptContext.Trigger,
        ["allow"] = ScriptContext.Trigger,
        ["modifier"] = ScriptContext.Modifier,
        ["targeted_modifier"] = ScriptContext.Modifier,
        ["research_bonus"] = ScriptContext.Plain,
        ["ai_will_do"] = ScriptContext.Weight,
        ["ai_chance"] = ScriptContext.Weight,
        ["mean_time_to_happen"] = ScriptContext.Weight,
    };

    // Flow control that keeps the surrounding kind of script (effects stay effects, triggers triggers).
    private static readonly HashSet<string> SameContextBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else_if", "else", "hidden_effect", "hidden_trigger", "AND", "OR", "NOT", "random", "random_list",
        "custom_trigger_tooltip", "custom_effect_tooltip", "effect_tooltip", "count_triggers",
    };

    // Words that point at another scope: ROOT = { … }, owner = { … }, var:x = { … }…
    private static readonly HashSet<string> ScopeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ROOT", "FROM", "PREV", "THIS", "OWNER", "CONTROLLER", "CAPITAL", "OCCUPIED", "capital_scope", "owner",
        "controller", "overlord", "faction_leader", "FROMFROM", "PREVPREV",
    };

    // Keys that are not commands but parameters of the enclosing one (tooltip, limit…), valid anywhere.
    private static readonly HashSet<string> Parameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "limit", "tooltip", "random_select_amount", "display_individual_scopes", "include_invisible", "days", "hours",
        "months", "value", "text", "amount", "chance", "factor", "add", "base", "modifier",
        // Event options
        "name", "original_recipient_only",
    };

    private readonly Dictionary<ScriptContext, Dictionary<string, ScriptCommand>> _commands = new();

    public ScriptSchema(IEnumerable<ScriptCommand> commands)
    {
        foreach (var command in commands)
        {
            if (!_commands.TryGetValue(command.Context, out var byName))
                _commands[command.Context] = byName = new Dictionary<string, ScriptCommand>(StringComparer.OrdinalIgnoreCase);
            byName.TryAdd(command.Name, command);
        }
    }

    public static ScriptSchema Empty { get; } = new([]);

    public bool IsLoaded => _commands.Count > 0;

    /// <summary>Reads the documentation shipped in <c>&lt;game&gt;/documentation</c>; empty when it is missing.</summary>
    public static ScriptSchema Load(string? gameRoot)
    {
        if (gameRoot is null)
            return Empty;
        var folder = Path.Combine(gameRoot, "documentation");
        IEnumerable<ScriptCommand> Read(string file, Func<string, IEnumerable<ScriptCommand>> parse)
        {
            var path = Path.Combine(folder, file);
            try
            {
                return File.Exists(path) ? parse(File.ReadAllText(path)).ToList() : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
        return new ScriptSchema(Read("effects_documentation.md", ScriptDocumentation.ParseEffects)
            .Concat(Read("triggers_documentation.md", ScriptDocumentation.ParseTriggers))
            .Concat(Read("modifiers_documentation.md", ScriptDocumentation.ParseModifiers)));
    }

    public ScriptCommand? Find(ScriptContext context, string name) =>
        _commands.TryGetValue(context, out var byName) ? byName.GetValueOrDefault(name) : null;

    public IEnumerable<ScriptCommand> All(ScriptContext context) =>
        _commands.TryGetValue(context, out var byName) ? byName.Values : [];

    /// <summary>The kind of script inside an entity's <paramref name="key"/> block (Plain when it isn't script).</summary>
    public static ScriptContext EntityBlock(string key) => EntityBlocks.GetValueOrDefault(key, ScriptContext.Plain);

    /// <summary>The kind of script inside a <paramref name="key"/> block nested in a <paramref name="parent"/> block.</summary>
    public ScriptContext ChildContext(ScriptContext parent, string key)
    {
        switch (parent)
        {
            case ScriptContext.Effect or ScriptContext.Trigger:
                if (key.Equals("limit", StringComparison.OrdinalIgnoreCase)
                    || parent == ScriptContext.Effect && key.Equals("trigger", StringComparison.OrdinalIgnoreCase))
                    return ScriptContext.Trigger; // also an event option's own trigger
                if (key.Equals("ai_chance", StringComparison.OrdinalIgnoreCase))
                    return ScriptContext.Weight;
                // random_list = { 50 = { modifier = { factor = 2 has_war = yes } … } }: weights with conditions.
                if (key.Equals("modifier", StringComparison.OrdinalIgnoreCase))
                    return ScriptContext.Trigger;
                if (SameContextBlocks.Contains(key) || IsScopeChange(key) || NumberKey().IsMatch(key))
                    return parent;
                // Any other documented command with a block (add_popularity = { ideology … },
                // ships_in_area = { area … }) holds its parameters, not script.
                return Find(parent, key) is not null ? ScriptContext.Plain : parent;
            case ScriptContext.Weight:
                // ai_will_do = { factor = 1 modifier = { factor = 0 has_war = yes } }
                return key.Equals("modifier", StringComparison.OrdinalIgnoreCase) ? ScriptContext.Trigger : ScriptContext.Plain;
            default:
                return ScriptContext.Plain;
        }
    }

    /// <summary>
    /// Whether <paramref name="key"/> switches scope rather than doing something: GER, 64,
    /// every_owned_state, ROOT, FROM.FROM, mio:…/sp:…/var:…/event_target:…
    /// </summary>
    public bool IsScopeChange(string key) =>
        ScopeWords.Contains(key) || TagKey().IsMatch(key) || NumberKey().IsMatch(key)
        || key.Contains(':') || key.Contains('.') || IteratorPrefix().IsMatch(key);

    /// <summary>
    /// A key that is probably a named scope target — a character, an operative… (<c>AFG_generic_navy_1 = { … }</c>),
    /// so a block under it is in an unknown scope rather than an unknown command.
    /// </summary>
    public static bool LooksLikeTarget(string key) => TargetKey().IsMatch(key);

    /// <summary>The scope inside <paramref name="key"/> when it is an iterator or a target: STATE, COUNTRY, CHARACTER, or null if unknown.</summary>
    public static string? ScopeOf(string key)
    {
        if (TagKey().IsMatch(key))
            return "COUNTRY";
        if (NumberKey().IsMatch(key))
            return "STATE";
        if (!IteratorPrefix().IsMatch(key))
            return null;
        if (key.Contains("state", StringComparison.OrdinalIgnoreCase))
            return "STATE";
        if (key.Contains("character", StringComparison.OrdinalIgnoreCase) || key.Contains("leader", StringComparison.OrdinalIgnoreCase)
            || key.Contains("operative", StringComparison.OrdinalIgnoreCase) || key.Contains("scientist", StringComparison.OrdinalIgnoreCase))
            return "CHARACTER";
        if (key.Contains("country", StringComparison.OrdinalIgnoreCase) || key.Contains("neighbor", StringComparison.OrdinalIgnoreCase)
            || key.Contains("allied", StringComparison.OrdinalIgnoreCase) || key.Contains("enemy", StringComparison.OrdinalIgnoreCase))
            return "COUNTRY";
        return null;
    }

    /// <summary>if, AND, OR, hidden_effect…: blocks that neither do nor check anything themselves.</summary>
    public static bool IsFlowControl(string key) => SameContextBlocks.Contains(key);

    /// <summary>Keys that are part of the language rather than commands: flow control, scope changes, parameters.</summary>
    public bool IsStructural(string key) => SameContextBlocks.Contains(key) || Parameters.Contains(key) || IsScopeChange(key);

    /// <summary>
    /// The script text a newly added command starts with: the documented required fields for
    /// block commands (<c>{ ideology = neutrality popularity = 0.05 }</c>), a sample value otherwise.
    /// Null when the documentation shows no example (<c>has_tech</c>): the value is better taken from scripts.
    /// </summary>
    public string? Template(ScriptCommand command)
    {
        if (command.Shape == ValueShape.Block)
        {
            var fields = command.Fields.Where(f => !f.Optional).ToList();
            if (fields.Count == 0)
                return "{ }";
            return "{ " + string.Join(" ", fields.Select(f => $"{f.Key} = {(f.Sample == "{ }" ? "{ }" : ScriptEdits.FormatValue(f.Sample))}")) + " }";
        }
        if (SameContextBlocks.Contains(command.Name) || IteratorPrefix().IsMatch(command.Name))
            return "{ }";
        return command.Shape switch
        {
            ValueShape.Number => command.Sample ?? "0",
            ValueShape.Bool => "yes",
            ValueShape.Text when command.Sample is { } sample => ScriptEdits.FormatValue(sample),
            _ => null,
        };
    }

    /// <summary>A Ukrainian category for the command picker, from the command's name.</summary>
    public static string Category(ScriptCommand command)
    {
        if (command.Context == ScriptContext.Modifier)
            return ModifierCategory(command.Category);
        var name = command.Name.ToLowerInvariant();
        if (SameContextBlocks.Contains(command.Name) || name is "always" or "limit")
            return "Логіка";
        if (IteratorPrefix().IsMatch(name))
            return "Області дії";
        if (Has(name, "event", "flag", "variable", "var_", "global_", "tooltip", "log"))
            return "Події й прапорці";
        if (Has(name, "war", "faction", "puppet", "annex", "guarantee", "opinion", "relation", "diplomatic", "tension",
                "wargoal", "claim", "core", "transfer_state", "alliance", "embargo", "peace", "autonomy", "subject", "occupied"))
            return "Дипломатія";
        if (Has(name, "army", "division", "unit", "equipment", "manpower", "experience", "_xp", "doctrine", "navy", "naval", "ship",
                "air", "plane", "operative", "leader_trait", "field_marshal", "commander", "military"))
            return "Армія";
        if (Has(name, "factory", "building", "resource", "research", "tech", "production", "industr", "infrastructure",
                "railway", "supply", "convoy", "fuel", "economy", "civilian"))
            return "Економіка";
        if (Has(name, "political", "stability", "war_support", "ideolog", "popularity", "idea", "party", "leader", "election",
                "government", "law", "advisor", "character", "coup", "civil_war", "focus", "decision", "mission"))
            return "Політика";
        return "Інше";
    }

    public static IReadOnlyList<string> Categories(ScriptContext context) => context == ScriptContext.Modifier
        ? ["Країна", "Політика", "Армія", "Флот", "Авіація", "Виробництво", "Розвідка", "Інше"]
        : ["Політика", "Армія", "Економіка", "Дипломатія", "Події й прапорці", "Області дії", "Логіка", "Інше"];

    private static string ModifierCategory(string? category) => category?.Split(',')[0].Trim().ToLowerInvariant() switch
    {
        "country" => "Країна",
        "politics" or "autonomy" or "government_in_exile" or "faction" => "Політика",
        "army" or "aggressive" or "defensive" or "military_advancements" or "unit_leader" => "Армія",
        "naval" => "Флот",
        "air" => "Авіація",
        "war_production" or "state" => "Виробництво",
        "intelligence_agency" => "Розвідка",
        _ => "Інше",
    };

    private static bool Has(string name, params string[] parts) => parts.Any(name.Contains);

    [GeneratedRegex("^[A-Z][A-Z0-9]{2}$")]
    private static partial Regex TagKey();

    [GeneratedRegex("^[A-Z]{3}_")]
    private static partial Regex TargetKey();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex NumberKey();

    [GeneratedRegex("^(every|random|any|all)_", RegexOptions.IgnoreCase)]
    private static partial Regex IteratorPrefix();
}
