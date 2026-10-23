using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.FileSystem;
using HoI4ModBuilder.Game.Schema;
using HoI4ModBuilder.Game.Tooltips;
using HoI4ModBuilder.ViewModels.Entities.Inspector;

namespace HoI4ModBuilder.Services;

/// <summary>
/// Effects and modifiers in words, in the chosen language: the in-game tooltip of an entity, and
/// the readable form of a single modifier or effect for the inspector.
/// </summary>
public sealed class TooltipService(LocalisationService localisation, ContentIndex content, ScriptWorkspace workspace,
    GameFileSystem fileSystem, string? gamePath) : ITooltipSource
{
    // Blocks of effects an entity runs, shown under their name.
    private static readonly string[] EffectBlocks =
        ["completion_reward", "select_effect", "complete_effect", "remove_effect", "timeout_effect", "on_add", "on_remove", "immediate"];

    // Files of ideas, parsed once per version of their text (a tree's tooltips name many ideas of few files).
    private readonly Dictionary<string, (string Text, SyntaxTree Tree)> _parsed = new(Paths.Comparer);

    public ModifierCatalog Modifiers { get; private set; } = ModifierCatalog.Empty;

    /// <summary>Raised on the UI thread once the modifier definitions are read.</summary>
    public event EventHandler? Changed;

    public async Task LoadAsync()
    {
        Modifiers = await Task.Run(() => ModifierCatalog.Load(gamePath, fileSystem));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string? Text(string key) => localisation.Index.Find(key);

    public BlockSyntax? IdeaModifiers(string idea)
    {
        foreach (var (path, entries) in content.Files)
        {
            if (!ContentParser.Flatten(entries).Any(e => e.Kind == ContentKind.Idea && e.Id == idea)
                || workspace.ReadText(path) is not { } text)
                continue;
            if (!_parsed.TryGetValue(path, out var parsed) || !ReferenceEquals(parsed.Text, text) && parsed.Text != text)
                _parsed[path] = parsed = (text, SyntaxTree.Parse(text));
            var block = ContentParser.Locate(parsed.Tree, workspace.Relative(path), ContentKind.Idea, idea)?.Value as BlockSyntax;
            return block is null ? null : ScriptRead.Blocks(block, "modifier").FirstOrDefault();
        }
        return null;
    }

    /// <summary>
    /// The entity's tooltip as the game would show it: its name, then what it does (modifiers, effects).
    /// Null when there is nothing beyond the name.
    /// </summary>
    /// <param name="contextKey">How the block is defined: <c>focus</c>, <c>option</c>, an event type…</param>
    public IReadOnlyList<TooltipLine>? For(string? contextKey, ContentKind? kind, string title, BlockSyntax block)
    {
        var describer = new EffectDescriber(this);
        var body = new List<TooltipLine>();

        if (contextKey == "option")
        {
            body.AddRange(describer.Describe(block));
        }
        else
        {
            if (contextKey == "focus" && ScriptRead.Scalar(block, "cost") is { } cost
                && decimal.TryParse(cost, System.Globalization.CultureInfo.InvariantCulture, out var weeks)
                && Text("FOCUS_STATUS_TOOLTIP") is { } time)
                body.Add(new TooltipLine(0, GameText.Render(time, new Dictionary<string, object> { ["DAYS"] = weeks * 7 })));

            foreach (var modifiers in ScriptRead.Blocks(block, "modifier"))
                body.AddRange(describer.Modifiers(modifiers));

            foreach (var key in EffectBlocks)
            {
                foreach (var effects in ScriptRead.Blocks(block, key))
                {
                    var lines = describer.Describe(effects, 1);
                    if (lines.Count == 0)
                        continue;
                    var header = key == "completion_reward" && Text("FOCUS_REWARD_TOOLTIP") is { } reward
                        ? GameText.Render(reward)
                        : [new TextRun(PropertyLabels.Label(key) + ":", 'H')];
                    body.Add(new TooltipLine(0, header));
                    body.AddRange(lines);
                }
            }

            // An event: what each option does.
            if (kind == ContentKind.Event)
            {
                foreach (var option in ScriptRead.Blocks(block, "option"))
                {
                    var name = ScriptRead.Scalar(option, "name");
                    body.Add(new TooltipLine(0, [new TextRun((name is null ? null : localisation.Text(name)) ?? name ?? "option", 'Y')]));
                    body.AddRange(describer.Describe(option, 1));
                }
            }

            // A scripted effect is a block of effects itself.
            if (kind == ContentKind.ScriptedEffect)
                body.AddRange(describer.Describe(block));
        }

        if (body.Count == 0)
            return null;
        return [new TooltipLine(0, [new TextRun(title, 'H')]), .. body];
    }

    /// <summary>
    /// A single modifier or effect in words, for its row in the inspector: "+5.0%" in green for a
    /// modifier, "Political Power: +100." for an effect. Null when there's nothing better than the code.
    /// </summary>
    public IReadOnlyList<TextRun>? Preview(ScriptContext context, PropertySyntax property)
    {
        if (property.Value is not ScalarSyntax scalar)
            return null;
        switch (context)
        {
            case ScriptContext.Modifier:
                return Modifiers.Value(property.Key.ValueText, scalar.ValueText) is { } run ? [run] : null;
            case ScriptContext.Effect:
            {
                // Only effects worded by the game's texts; the rest would just repeat the code.
                var lines = new EffectDescriber(this).DescribeKnown(property);
                return lines.Count == 1 ? lines[0].Runs : null;
            }
            default:
                return null;
        }
    }

    /// <summary>A modifier's name in the chosen language, for its inspector label.</summary>
    public string? ModifierName(string key) =>
        ModifierCatalog.Name(key, Text) is { } name ? GameText.Plain(GameText.Render(name)) : null;
}
