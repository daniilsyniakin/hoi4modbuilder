using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.FileSystem;

namespace HoI4ModBuilder.Game.Tooltips;

/// <summary>How a modifier is shown: as a percentage or a number, with how many decimals, and whether more of it is good.</summary>
/// <param name="Good">Whether a positive value is good (green); null for neutral ones (yellow).</param>
public sealed record ModifierInfo(bool IsPercent, int Precision, bool? Good, bool IsBoolean = false);

/// <summary>
/// What the game knows about modifiers: the precision from its documentation, the mod's and game's
/// own definitions (<c>common/modifier_definitions</c>), and for the engine's built-in modifiers —
/// whose display rules aren't published — rules from their names.
/// </summary>
public sealed partial class ModifierCatalog
{
    // Built-in modifiers that the name rules get wrong.
    private static readonly HashSet<string> Numbers = new(StringComparer.OrdinalIgnoreCase)
    {
        "army_org", "army_morale", "max_dig_in", "civilian_factory_use", "political_power_cost", "operative_slot",
        "decryption", "encryption", "air_volunteer_cap", "weekly_manpower", "monthly_manpower", "research_sharing_per_country_bonus_factor",
        "command_power_gain", "autonomy_gain", "experience_gain_army", "experience_gain_navy", "experience_gain_air",
        "max_command_power", "military_leader_cost", "naval_detection", "dig_in_speed", "max_planning_factor",
    };

    private static readonly HashSet<string> Percents = new(StringComparer.OrdinalIgnoreCase)
    {
        "max_planning", "monthly_population", "justify_war_goal_time", "enemy_justify_war_goal_time",
        "army_org_regain", "special_forces_cap", "non_core_manpower", "conscription",
    };

    private static readonly string[] BadWords =
    [
        "cost", "consumer_goods", "attrition", "_tension", "training_time", "justify_war_goal_time", "consumption",
        "lack_of_resource", "accidents", "resistance_growth", "resistance_target", "civilian_factory_use", "upkeep",
        "required", "foreign_subversive", "penalty", "surrender_progress", "war_support_reduction", "stability_reduction",
    ];

    private readonly Dictionary<string, int> _precision;
    private readonly Dictionary<string, ModifierInfo> _defined;
    private readonly HashSet<string> _booleans;

    private ModifierCatalog(Dictionary<string, int> precision, HashSet<string> booleans, Dictionary<string, ModifierInfo> defined)
    {
        _precision = precision;
        _booleans = booleans;
        _defined = defined;
    }

    public static ModifierCatalog Empty { get; } = new([], [], []);

    /// <summary>Reads the documentation (<paramref name="gameRoot"/>) and the modifier definitions of the game and the mod.</summary>
    public static ModifierCatalog Load(string? gameRoot, GameFileSystem? fileSystem)
    {
        var precision = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var booleans = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documentation = gameRoot is null ? null : Path.Combine(gameRoot, "documentation", "modifiers_documentation.md");
        if (documentation is not null && File.Exists(documentation))
        {
            try
            {
                foreach (Match match in DocumentedModifier().Matches(File.ReadAllText(documentation)))
                {
                    var name = match.Groups["name"].Value;
                    if (match.Groups["boolean"].Success)
                        booleans.Add(name);
                    else
                        precision[name] = int.Parse(match.Groups["digits"].Value, CultureInfo.InvariantCulture);
                }
            }
            catch (IOException)
            {
                // Without the documentation numbers keep their own decimals.
            }
        }

        var defined = new Dictionary<string, ModifierInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in fileSystem?.EnumerateFiles("common/modifier_definitions", "*.txt") ?? [])
        {
            string text;
            try
            {
                text = Encoding.UTF8.GetString(File.ReadAllBytes(file.FullPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var property in SyntaxTree.Parse(text).Root.Properties())
            {
                if (property.Value is not BlockSyntax block)
                    continue;
                var type = ScriptRead.Scalar(block, "value_type");
                var colour = ScriptRead.Scalar(block, "color_type");
                defined[property.Key.ValueText] = new ModifierInfo(
                    IsPercent: type is "percentage" or "percentage_in_hundred",
                    Precision: int.TryParse(ScriptRead.Scalar(block, "precision"), out var digits) ? digits : 1,
                    Good: colour switch { "good" => true, "bad" => false, _ => null },
                    IsBoolean: type == "yes_no");
            }
        }
        return new ModifierCatalog(precision, booleans, defined);
    }

    public ModifierInfo Info(string key)
    {
        if (_defined.TryGetValue(key, out var defined))
            return defined;
        var name = key.ToLowerInvariant();
        var isPercent = Percents.Contains(name)
                        || (!Numbers.Contains(name) && !name.EndsWith("_gain") && !name.StartsWith("max_")
                            && !name.Contains("slot") && !name.EndsWith("_cap") && !name.Contains("intel_to_others"));
        var bad = BadWords.Any(name.Contains) && !name.Contains("reduction") && !name.StartsWith("enemy_");
        return new ModifierInfo(isPercent, _precision.GetValueOrDefault(key, isPercent ? 1 : 2), !bad, _booleans.Contains(key));
    }

    /// <summary>The modifier's name in the game's language (<c>MODIFIER_STABILITY_FACTOR</c> → "Stability").</summary>
    /// <param name="text">Looks up a localisation key, raw (with colour codes).</param>
    public static string? Name(string key, Func<string, string?> text) =>
        text("MODIFIER_" + key.ToUpperInvariant()) ?? text(key);

    /// <summary>"Stability: +5.00%", the value coloured by whether it is good. Null for values that aren't numbers.</summary>
    public TooltipLine? Line(string key, string value, Func<string, string?> text, int indent = 0)
    {
        var info = Info(key);
        var name = Name(key, text) is { } localised
            ? GameText.Plain(GameText.Render(localised))
            : key;
        if (info.IsBoolean || value is "yes" or "no")
            return new TooltipLine(indent, [new TextRun(name)]);
        if (Value(key, value) is not { } run)
            return null;
        return new TooltipLine(indent, [new TextRun(name + ": "), run]);
    }

    /// <summary>The value as the game shows it ("+5.00%", "-0.10"), coloured; null when it isn't a number.</summary>
    public TextRun? Value(string key, string value)
    {
        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return null;
        var info = Info(key);
        var run = GameText.Number(number, $"={info.Precision}{(info.IsPercent ? "%" : "")}");
        char? colour = number == 0 ? null : info.Good is { } good ? ((number > 0) == good ? 'G' : 'R') : 'Y';
        return run with { Colour = colour };
    }

    [GeneratedRegex(@"^## (?<name>\S+)\s*\n\s*\n\* (?:(?<boolean>Boolean)|Number with (?<digits>\d+) decimal places)", RegexOptions.Multiline)]
    private static partial Regex DocumentedModifier();
}
