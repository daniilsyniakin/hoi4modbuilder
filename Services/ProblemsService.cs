using System.Diagnostics;
using Avalonia.Threading;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Completion;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.Schema;
using HoI4ModBuilder.Game.Validation;

namespace HoI4ModBuilder.Services;

/// <summary>
/// Checks the whole mod in the background shortly after anything changes, and keeps the problems
/// found (by entity, for the graphical editors and the content tree).
/// </summary>
public sealed class ProblemsService
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(600);

    private readonly ScriptWorkspace _workspace;
    private readonly ContentIndex _content;
    private readonly GameAssets _assets;
    private readonly LocalisationService _localisation;
    private readonly DispatcherTimer _timer;
    private Dictionary<(ContentKind, string), List<Problem>> _byEntity = new();
    private ScriptVocabulary? _gameVocabulary;
    private int _generation;

    public ProblemsService(ScriptWorkspace workspace, ContentIndex content, GameAssets assets, LocalisationService localisation)
    {
        _workspace = workspace;
        _content = content;
        _assets = assets;
        _localisation = localisation;
        _timer = new DispatcherTimer { Interval = Delay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Stop();
            await RunAsync();
        };

        workspace.Changed += _ => Schedule();
        content.Changed += (_, _) => Schedule();
        localisation.Changed += (_, _) => Schedule();
        assets.SpritesChanged += (_, _) => Schedule();
    }

    /// <summary>The game's documented effects, triggers and modifiers; empty until loaded.</summary>
    public ScriptSchema Schema { get; private set; } = ScriptSchema.Empty;

    public IReadOnlyList<Problem> Problems { get; private set; } = [];

    public int ErrorCount { get; private set; }

    public int WarningCount { get; private set; }

    /// <summary>Raised on the UI thread after each check.</summary>
    public event EventHandler? Changed;

    /// <summary>Loads the documentation and the game's vocabulary, then runs the first check.</summary>
    public async Task LoadAsync(string? gamePath)
    {
        var schema = Task.Run(() => ScriptSchema.Load(GameLocator.IsGameDirectory(gamePath) ? gamePath : null));
        var vocabulary = VocabularyService.LoadGameAsync(gamePath);
        try
        {
            Schema = await schema;
            _gameVocabulary = await vocabulary;
        }
        catch (Exception ex)
        {
            // Without them the checks are less thorough, but still useful.
            Trace.WriteLine($"Validation data failed to load: {ex.Message}");
        }
        Schedule();
    }

    public IReadOnlyList<Problem> For(ContentKind kind, string id) =>
        _byEntity.TryGetValue((kind, id), out var problems) ? problems : [];

    /// <summary>The worst severity of an entity's problems, or null when it has none.</summary>
    public ProblemSeverity? WorstFor(ContentKind kind, string id) =>
        _byEntity.TryGetValue((kind, id), out var problems) ? problems.Max(p => p.Severity) : null;

    public void Schedule()
    {
        _timer.Stop();
        _timer.Start();
    }

    private async Task RunAsync()
    {
        if (!_content.IsLoaded)
        {
            Schedule();
            return;
        }
        var generation = ++_generation;

        // Texts are read here: open tabs must be read on the UI thread.
        var texts = _content.Files.Keys
            .Select(file => (File: file, Text: _workspace.ReadText(file)))
            .Where(f => f.Text is not null)
            .ToList();
        var context = CreateContext();
        var root = _workspace.RootPath;

        List<Problem> problems;
        try
        {
            problems = await Task.Run(() =>
            {
                var parsed = texts
                    .Select(f => (f.File, Relative: Path.GetRelativePath(root, f.File).Replace('\\', '/'), Tree: SyntaxTree.Parse(f.Text!)))
                    .ToList();
                return ModValidator.Validate(parsed, context);
            });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Validation failed: {ex}");
            return;
        }
        if (generation != _generation)
            return; // a newer check is on its way

        Problems = problems
            .OrderByDescending(p => p.Severity)
            .ThenBy(p => p.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Position)
            .ToList();
        ErrorCount = problems.Count(p => p.Severity == ProblemSeverity.Error);
        WarningCount = problems.Count - ErrorCount;
        _byEntity = problems
            .Where(p => p.Kind is not null && p.EntityId is not null)
            .GroupBy(p => (p.Kind!.Value, p.EntityId!))
            .ToDictionary(g => g.Key, g => g.ToList());
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private ValidationContext CreateContext()
    {
        var sprites = _assets.Sprites;
        var index = _localisation.Index;
        var vocabulary = _gameVocabulary;
        var scriptedEffects = vocabulary?.GetKeys(ScriptVocabulary.RootContext("common/scripted_effects"));
        var scriptedTriggers = vocabulary?.GetKeys(ScriptVocabulary.RootContext("common/scripted_triggers"));
        return new ValidationContext(
            Schema,
            sprites.Count > 0 ? name => sprites.Find(name) is not null : null,
            _localisation.Language is not null ? key => index.Find(key) is not null : null,
            (blockKey, key) => vocabulary?.GetKeys(ScriptVocabulary.BlockContext(blockKey))?.ContainsKey(key) == true,
            (key, kind) => (kind == ScriptContext.Effect ? scriptedEffects : scriptedTriggers)?.ContainsKey(key) == true);
    }
}
