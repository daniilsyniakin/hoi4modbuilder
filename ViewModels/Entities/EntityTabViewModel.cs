using System.Globalization;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Completion;
using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Clausewitz.Syntax;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.Schema;
using HoI4ModBuilder.Game.Validation;
using HoI4ModBuilder.Services;
using HoI4ModBuilder.ViewModels.Entities.Inspector;

namespace HoI4ModBuilder.ViewModels.Entities;

/// <summary>Services a graphical editor needs, plus ways to leave it (to code or another entity).</summary>
/// <param name="Rename">Starts renaming an entity; true = also update references (F2), false = only the definition.</param>
public sealed record EntityContext(
    ScriptWorkspace Workspace,
    ContentIndex Content,
    GameAssets Assets,
    LocalisationService Localisation,
    LocalisationEditor Texts,
    ProblemsService Problems,
    Func<ScriptVocabulary?> Vocabulary,
    Action<string, int> OpenInCode,
    Action<ContentKind, string> OpenEntity,
    Func<ContentKind, string, bool, Task> Rename,
    Action<string?> ReportError);

/// <summary>
/// A tab showing a game entity graphically (focus tree, event graph, idea card…). It re-reads the
/// script whenever the file changes — through its own edits, typing in a text tab or on disk — so
/// the picture and the code never disagree.
/// </summary>
public abstract partial class EntityTabViewModel : DocumentTabViewModel
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(200);
    private static readonly IReadOnlySet<string> NoKeys = new HashSet<string>();

    private readonly DispatcherTimer _refreshTimer;
    private readonly Dictionary<string, bool> _expansion = new();
    private bool _isActive;
    private bool _stale = true;
    private bool _disposed;

    protected EntityTabViewModel(EntityContext context, ContentKind kind, string id, string filePath, Action<DocumentTabViewModel> close)
        : base(filePath, context.Workspace.RootPath, close)
    {
        Context = context;
        EntityContentKind = kind;
        EntityId = id;

        _refreshTimer = new DispatcherTimer { Interval = RefreshDelay };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            RefreshNow();
        };
        context.Workspace.Changed += OnSourceChanged;
        context.Content.Changed += OnServiceChanged;
        context.Localisation.Changed += OnServiceChanged;
        context.Assets.SpritesChanged += OnServiceChanged;
        context.Problems.Changed += OnServiceChanged;
    }

    protected EntityContext Context { get; }

    public ContentKind EntityContentKind { get; }

    public string EntityId { get; private set; }

    /// <summary>Identifies the tab, so opening the same entity again activates it.</summary>
    public string Key => $"{EntityContentKind}:{EntityId}";

    public override ContentKind? EntityKind => EntityContentKind;

    public override string Name => EntityId;

    public override string Breadcrumbs => $"{KindTitle}  ›  {EntityId}  ·  {Context.Workspace.Relative(FilePath)}";

    protected abstract string KindTitle { get; }

    /// <summary>Shown instead of the editor when the entity can't be read (e.g. it was deleted).</summary>
    [ObservableProperty]
    public partial string? Error { get; protected set; }

    /// <summary>The inspector for the selected object.</summary>
    [ObservableProperty]
    public partial PropertyPanelViewModel? Panel { get; private set; }

    /// <summary>Pan and zoom of the graph view, kept while switching tabs. Null until first shown.</summary>
    public Matrix? ViewTransform { get; set; }

    /// <summary>What F2 renames: the selected object, or the tab's own entity.</summary>
    public virtual (ContentKind Kind, string Id)? RenameTarget => (EntityContentKind, EntityId);

    /// <summary>Only the visible tab re-reads its files; hidden ones catch up when shown.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            if (value && _stale)
                RefreshNow();
        }
    }

    public void ScheduleRefresh()
    {
        _stale = true;
        if (!_isActive || _disposed)
            return;
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    public void RefreshNow()
    {
        if (_disposed)
            return;
        _refreshTimer.Stop();
        _stale = false;
        Refresh();
    }

    /// <summary>Re-reads the entity from the current text and rebuilds the view model.</summary>
    protected abstract void Refresh();

    /// <summary>An entity was renamed; follow it if it is this tab's or the selected one.</summary>
    public virtual void OnRenamed(ContentKind kind, string oldId, string newId)
    {
        if (kind != EntityContentKind || EntityId != oldId)
            return;
        EntityId = newId;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Key));
        OnPropertyChanged(nameof(Breadcrumbs));
    }

    public override void ReloadFromDisk() => ScheduleRefresh();

    public override void Discard()
    {
        if (_disposed)
            return;
        _disposed = true;
        _refreshTimer.Stop();
        Context.Workspace.Changed -= OnSourceChanged;
        Context.Content.Changed -= OnServiceChanged;
        Context.Localisation.Changed -= OnServiceChanged;
        Context.Assets.SpritesChanged -= OnServiceChanged;
        Context.Problems.Changed -= OnServiceChanged;
    }

    /// <summary>Whether a change to <paramref name="path"/> can affect this view.</summary>
    protected virtual bool DependsOn(string path) => Paths.AreEqual(path, FilePath);

    private void OnSourceChanged(string path)
    {
        if (DependsOn(path))
            ScheduleRefresh();
    }

    private void OnServiceChanged(object? sender, EventArgs e) => ScheduleRefresh();

    // ── Helpers for subclasses ────────────────────────────────

    /// <summary>The current text and tree of a file, or null if it can't be read.</summary>
    protected (string Text, SyntaxTree Tree)? Read(string path) =>
        Context.Workspace.ReadText(path) is { } text ? (text, SyntaxTree.Parse(text)) : null;

    /// <summary>Starts a named undo step; edits until disposal undo together ("Переміщення фокуса").</summary>
    protected IDisposable Action(string description) => Context.Workspace.Action(description);

    /// <summary>Applies an edit to <paramref name="path"/> and reports a failure in the status bar.</summary>
    protected void Edit(string path, Func<string, SyntaxTree, IEnumerable<TextEdit>> build) =>
        Context.ReportError(Context.Workspace.Edit(path, build));

    /// <summary>Sets <c>key = value</c> in the block <paramref name="locate"/> finds (adding it when absent).</summary>
    protected void SetProperty(string path, Func<SyntaxTree, BlockSyntax?> locate, string key, string value) =>
        Edit(path, (text, tree) => locate(tree) is { } block ? [ScriptEdits.SetProperty(text, block, key, value)] : []);

    /// <summary>Locates an entity's block in a fresh tree of <paramref name="path"/>.</summary>
    protected Func<SyntaxTree, BlockSyntax?> EntityLocator(string path, ContentKind kind, string id) =>
        tree => ContentParser.Locate(tree, Context.Workspace.Relative(path), kind, id)?.Value as BlockSyntax;

    /// <summary>Loads a sprite or texture and hands it over when ready (pictures decode in the background).</summary>
    protected async void LoadPicture(string? spriteOrPath, Action<Avalonia.Media.IImage?> apply)
    {
        if (string.IsNullOrWhiteSpace(spriteOrPath))
            return;
        var image = await Context.Assets.LoadPictureAsync(spriteOrPath);
        if (!_disposed)
            apply(image);
    }

    /// <summary>A click-to-change picture choosing among sprites whose names start with <paramref name="prefixes"/>.</summary>
    protected PictureEditor SpritePicker(IReadOnlyList<string> prefixes, Action<string> apply) =>
        new(Context.Assets, prefixes, apply);

    /// <summary>Builds the inspector for an entity block.</summary>
    /// <param name="identity">The object; a panel for the same object is updated in place.</param>
    /// <param name="contextKey">The key the block is defined with, for key and value suggestions.</param>
    /// <param name="skip">Properties edited elsewhere (e.g. the icon, changed by clicking the picture).</param>
    protected PropertyPanelViewModel BuildPanel(
        string path,
        string text,
        BlockSyntax block,
        Func<SyntaxTree, BlockSyntax?> locate,
        string identity,
        string title,
        string subtitle,
        int position,
        IReadOnlyList<SchemaProperty> schema,
        string? contextKey,
        string? description = null,
        string? picture = null,
        PictureEditor? pictureEditor = null,
        IReadOnlySet<string>? skip = null,
        IReadOnlyDictionary<string, FieldPicker>? pickers = null,
        string? titleKey = null,
        string? descriptionKey = null,
        (ContentKind Kind, string Id)? entity = null,
        string pathPrefix = "",
        ScriptContext rootContext = ScriptContext.Plain)
    {
        // The entity's problems, by path relative to this block (an option's block sits at option#N of its event).
        var all = entity is { } owner ? Context.Problems.For(owner.Kind, owner.Id) : [];
        var prefix = pathPrefix.Length == 0 ? "" : pathPrefix + "/";
        var problems = new Dictionary<string, Problem>();
        foreach (var problem in all.Where(p => p.Path.StartsWith(prefix, StringComparison.Ordinal) && p.Path.Length > prefix.Length))
        {
            var relative = problem.Path[prefix.Length..];
            if (!problems.TryGetValue(relative, out var known) || known.Severity < problem.Severity)
                problems[relative] = problem;
        }

        var editor = new ScriptBlockEditor(this, path, locate);
        var builder = new InspectorBuilder(text, editor, new Suggestions(Context), identity, _expansion, pickers, problems);
        var panel = new PropertyPanelViewModel(identity, title, subtitle, () => Context.OpenInCode(path, position))
        {
            Description = description,
            PictureName = picture,
            PictureEditor = pictureEditor,
            Root = builder.BuildRoot(block, contextKey, schema, skip ?? NoKeys, rootContext),
            // Problems of the object as a whole (a missing name, a missing sprite) head the panel.
            Problems = all.Where(p => p.Path == pathPrefix).ToList(),
            TitleText = LocalisedText(titleKey, placeholder: title),
            DescriptionText = LocalisedText(descriptionKey, placeholder: "Опис"),
        };
        LoadPicture(picture, image =>
        {
            panel.Picture = image;
            // The panel may have been merged into the one already shown.
            if (Panel is { } shown && shown != panel && shown.Identity == identity && shown.PictureName == picture)
                shown.Picture = image;
        });
        return panel;
    }

    /// <summary>An editable localised text, or null when texts can't be edited right now (no language chosen).</summary>
    protected LocalisedTextViewModel? LocalisedText(string? key, string placeholder) =>
        key is null || !Context.Texts.CanEdit
            ? null
            : new LocalisedTextViewModel(key, Context.Texts.EditableText(key) ?? "", Context.Texts.FallbackText(key) ?? placeholder,
                text => Context.ReportError(Context.Texts.SetText(key, text)));

    /// <summary>Shows a panel, updating the current one in place when it describes the same object.</summary>
    protected void ShowPanel(PropertyPanelViewModel? panel)
    {
        if (panel is not null && Panel is { } shown && shown.Identity == panel.Identity)
            shown.UpdateFrom(panel);
        else
            Panel = panel;
    }

    /// <summary>Inspector edits addressed by key paths inside an entity block.</summary>
    private sealed class ScriptBlockEditor(EntityTabViewModel tab, string path, Func<SyntaxTree, BlockSyntax?> locate) : IInspectorEditor
    {
        public void SetValue(InspectorPath property, string value)
        {
            using var action = tab.Action($"Зміна «{PropertyLabels.Label(property.Steps[^1].Key)}»");
            tab.Edit(path, (_, tree) => Find(tree, property) is { Value: ScalarSyntax } found ? [ScriptEdits.ReplaceValue(found, value)] : []);
        }

        public void AddProperty(InspectorPath block, string key, string valueText)
        {
            using var action = tab.Action($"Додано «{PropertyLabels.Label(key)}»");
            tab.Edit(path, (text, tree) => FindBlock(tree, block) is { } found ? [ScriptEdits.InsertStatement(text, found, $"{key} = {valueText}")] : []);
        }

        public void Remove(InspectorPath property)
        {
            using var action = tab.Action($"Видалено «{PropertyLabels.Label(property.Steps[^1].Key)}»");
            tab.Edit(path, (text, tree) => Find(tree, property) is { } found ? [ScriptEdits.RemoveStatement(text, found)] : []);
        }

        public void AddItem(InspectorPath block, string value)
        {
            using var action = tab.Action($"Додано «{value}»");
            tab.Edit(path, (text, tree) => FindBlock(tree, block) is { } found ? [ScriptEdits.InsertStatement(text, found, ScriptEdits.FormatValue(value))] : []);
        }

        public void RemoveItem(InspectorPath block, int index)
        {
            using var action = tab.Action("Видалено елемент списку");
            tab.Edit(path, (text, tree) =>
                FindBlock(tree, block)?.Statements.OfType<ElementSyntax>().ElementAtOrDefault(index) is { } item
                    ? [ScriptEdits.RemoveStatement(text, item)]
                    : []);
        }

        private PropertySyntax? Find(SyntaxTree tree, InspectorPath target)
        {
            var block = locate(tree);
            PropertySyntax? property = null;
            foreach (var (key, occurrence) in target.Steps)
            {
                if (block is null)
                    return null;
                property = ScriptRead.All(block, key).ElementAtOrDefault(occurrence);
                block = property?.Value as BlockSyntax;
            }
            return property;
        }

        private BlockSyntax? FindBlock(SyntaxTree tree, InspectorPath target) =>
            target.Steps.IsEmpty ? locate(tree) : Find(tree, target)?.Value as BlockSyntax;
    }

    /// <summary>Keys and values learned from the game's scripts, plus the mod's own entities.</summary>
    private sealed class Suggestions(EntityContext context) : IInspectorSuggestions
    {
        private const int MaxSuggestions = 400;

        private Dictionary<ContentKind, List<string>>? _ids;
        private readonly Dictionary<ScriptContext, IReadOnlyList<CommandChoice>> _commands = [];

        public ScriptSchema Schema => context.Problems.Schema;

        public IReadOnlyList<CommandChoice> Commands(ScriptContext scriptContext)
        {
            if (_commands.TryGetValue(scriptContext, out var known))
                return known;
            var vocabulary = context.Vocabulary();
            int Popularity(string name) => vocabulary?.GlobalKeys.GetValueOrDefault(name)?.Count ?? 0;

            var documented = Schema.All(scriptContext)
                .Select(c => (Choice: new CommandChoice(c.Name, ScriptSchema.Category(c), c.Description, Schema.Template(c)), Count: Popularity(c.Name)));
            // The mod's own scripted effects (or triggers) are commands too.
            var modKind = scriptContext switch
            {
                ScriptContext.Effect => ContentKind.ScriptedEffect,
                ScriptContext.Trigger => ContentKind.ScriptedTrigger,
                _ => (ContentKind?)null,
            };
            var own = modKind is { } kind
                ? Ids(kind).Select(id => (Choice: new CommandChoice(id, InspectorBuilder.ModScriptsCategory, "", "yes"), Count: int.MaxValue))
                : [];
            return _commands[scriptContext] = own.Concat(documented)
                .OrderByDescending(c => c.Count)
                .ThenBy(c => c.Choice.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => c.Choice)
                .ToList();
        }

        public IReadOnlyList<string> Keys(string? blockKey)
        {
            if (blockKey is null || context.Vocabulary()?.GetKeys(ScriptVocabulary.BlockContext(blockKey)) is not { } keys)
                return [];
            return keys.OrderByDescending(k => k.Value.Count).Select(k => k.Key).Take(MaxSuggestions).ToList();
        }

        public IReadOnlyList<string> Values(string? blockKey, string key)
        {
            var result = new List<string>();
            if (EntitySchemas.ReferenceOf(key) is { } kind)
                result.AddRange(Ids(kind));
            if (Usage(blockKey, key) is { } usage)
                result.AddRange(usage.Values.OrderByDescending(v => v.Value).Select(v => v.Key));
            return result.Distinct(StringComparer.Ordinal).Take(MaxSuggestions).ToList();
        }

        public string DefaultValue(string? blockKey, string key)
        {
            var usage = Usage(blockKey, key);
            if (usage is { IsMostlyBlock: true })
                return "{ }";
            // Numbers aren't recorded as values: a key mostly seen with them has few recorded uses.
            var recorded = usage?.Values.Values.Sum() ?? 0;
            if (usage is not null && recorded * 2 < usage.Count - usage.BlockCount)
                return "0";
            if (usage?.Values.OrderByDescending(v => v.Value).FirstOrDefault().Key is { } common)
                return ScriptEdits.FormatValue(common);
            if (EntitySchemas.ReferenceOf(key) is { } kind && Ids(kind).FirstOrDefault() is { } id)
                return id;
            // Keys seen only with numbers record no values.
            return usage is not null ? "0" : "yes";
        }

        public string? Describe(string value)
        {
            if (value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase)
                || decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return null;
            return context.Localisation.Text(value);
        }

        private KeyUsage? Usage(string? blockKey, string key)
        {
            var vocabulary = context.Vocabulary();
            if (vocabulary is null)
                return null;
            return (blockKey is null ? null : vocabulary.GetKeys(ScriptVocabulary.BlockContext(blockKey))?.GetValueOrDefault(key))
                   ?? vocabulary.GlobalKeys.GetValueOrDefault(key);
        }

        private List<string> Ids(ContentKind kind)
        {
            _ids ??= context.Content.Files.Values
                .SelectMany(ContentParser.Flatten)
                .GroupBy(e => e.Kind)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Id).Distinct().OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList());
            return _ids.GetValueOrDefault(kind) ?? [];
        }
    }
}
