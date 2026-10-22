using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Clausewitz.Completion;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.Imaging;
using HoI4ModBuilder.Models;
using HoI4ModBuilder.Services;
using HoI4ModBuilder.ViewModels.Entities;

namespace HoI4ModBuilder.ViewModels;

/// <summary>The editor workspace for an opened mod.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    // Some programs save by deleting and re-creating the file; wait before treating a delete as final.
    private static readonly TimeSpan DeleteGracePeriod = TimeSpan.FromMilliseconds(300);

    private readonly SettingsStore _settings;
    private readonly IPlatformService _platform;
    private readonly IWorkspaceHost _host;
    private readonly ModFileWatcher? _watcher;
    private bool _disposed;

    public MainWindowViewModel(ModProject mod, SettingsStore settings, IPlatformService platform, IWorkspaceHost host)
    {
        Mod = mod;
        _settings = settings;
        _platform = platform;
        _host = host;
        Content = new ContentIndex(mod.RootPath);
        ProjectTree = new ProjectTreeViewModel(mod, Content);
        Assets = new GameAssets(settings.Settings.GamePath, mod.RootPath);
        Localisation = new LocalisationService(Assets.FileSystem, settings.Settings.Language);
        LanguageOptions = LocalisationService.Languages
            .Select(l => new LanguageOptionViewModel(l, l.Code == Localisation.Language, SetLanguage))
            .ToList();
        Workspace = new ScriptWorkspace(mod.RootPath, path => FindTab(path) as EditorTabViewModel);
        Workspace.History.Changed += (_, _) => OnHistoryChanged();
        Problems = new ProblemsService(Workspace, Content, Assets, Localisation);
        _creator = new EntityCreator(Workspace, Content, platform, mod.Name);
        CreateOptions = EntityTemplates.Creatable
            .Select(kind => new CreateOptionViewModel(kind, EntityCreator.Title(kind), CreateEntityCommand))
            .ToList();
        ProjectTree.AttachProblems(Problems);
        Problems.Changed += (_, _) => OnProblemsChanged();
        _entityContext = new EntityContext(Workspace, Content, Assets, Localisation,
            new LocalisationEditor(Workspace, Localisation, mod.Name),
            Problems,
            () => Vocabulary,
            (path, offset) => OpenFile(path, offset),
            (kind, id) => OpenEntity(kind, id),
            RenameAsync,
            error => StatusMessage = error);

        try
        {
            _watcher = new ModFileWatcher(mod.RootPath);
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnEntryCreated;
            _watcher.Deleted += OnEntryDeleted;
            _watcher.Renamed += OnEntryRenamed;
            _watcher.Overflow += OnWatcherOverflow;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            StatusMessage = $"Не вдалося стежити за змінами файлів: {ex.Message}";
        }

        _ = LoadVocabularyAsync();
        _ = Assets.LoadSpritesAsync();
        _ = Content.LoadAsync();
        _ = Localisation.LoadAsync();
        _ = Problems.LoadAsync(settings.Settings.GamePath);
    }

    /// <summary>Mistakes found in the mod, for the problems panel and the editors.</summary>
    public ProblemsService Problems { get; }

    // ── Creating entities ──────────────────────────────────────

    private readonly EntityCreator _creator;

    /// <summary>The "create" menu: one entry per kind that can be created.</summary>
    public IReadOnlyList<CreateOptionViewModel> CreateOptions { get; }

    /// <summary>
    /// Creates an entity of <paramref name="kind"/> where the content tree selection suggests
    /// (the selected namespace, slot, category or the selected entity's file) and opens it.
    /// </summary>
    [RelayCommand]
    private async Task CreateEntity(ContentKind kind)
    {
        var target = TargetFor(kind, ProjectTree.IsContentMode ? ProjectTree.SelectedContentNode : null);
        if (await _creator.CreateAsync(kind, target, error => StatusMessage = error) is not { } created)
            return;
        OpenEntity(created.Kind, created.Id, created.File);
    }

    private static CreationTarget TargetFor(ContentKind kind, ContentNodeViewModel? node)
    {
        // Only a selection of the same kind says where to put it (a focus counts for its tree's file).
        var nodeKind = node?.GroupKind == ContentKind.Focus ? ContentKind.FocusTree : node?.GroupKind;
        if (node is null || nodeKind != kind)
            return CreationTarget.None;
        return new CreationTarget(node.FilePath, kind == ContentKind.Technology ? null : node.Group);
    }

    // ── Problems panel ─────────────────────────────────────────

    [ObservableProperty]
    public partial bool IsProblemsPanelVisible { get; set; }

    public IReadOnlyList<ProblemRowViewModel> ProblemRows { get; private set; } = [];

    public int ErrorCount => Problems.ErrorCount;

    public int WarningCount => Problems.WarningCount;

    public bool HasErrors => Problems.ErrorCount > 0;

    public bool HasWarnings => Problems.WarningCount > 0;

    public bool HasNoProblems => Problems.ErrorCount + Problems.WarningCount == 0;

    [RelayCommand]
    private void ToggleProblemsPanel() => IsProblemsPanelVisible = !IsProblemsPanelVisible;

    /// <summary>Goes to a problem: the entity's graphical editor, or the code for problems outside entities.</summary>
    [RelayCommand]
    private void OpenProblem(ProblemRowViewModel? row)
    {
        if (row?.Problem is not { } problem)
            return;
        if (problem is { Kind: { } kind, EntityId: { } id })
            OpenEntity(kind, id);
        else
            OpenFile(problem.File, problem.Position);
    }

    private void OnProblemsChanged()
    {
        ProblemRows = Problems.Problems.Select(p => new ProblemRowViewModel(p, Workspace.Relative(p.File))).ToList();
        OnPropertyChanged(nameof(ProblemRows));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasNoProblems));
    }

    private readonly EntityContext _entityContext;

    /// <summary>Texts for localisation keys, for names in the graphical editors.</summary>
    public LocalisationService Localisation { get; }

    /// <summary>Reads and edits script files on behalf of the graphical editors.</summary>
    public ScriptWorkspace Workspace { get; }

    /// <summary>The status-bar choice of the language names are shown in.</summary>
    public IReadOnlyList<LanguageOptionViewModel> LanguageOptions { get; }

    public string LanguageShort => LanguageOptions.First(o => o.IsSelected).Language.Short;

    private void SetLanguage(LanguageOptionViewModel option)
    {
        foreach (var other in LanguageOptions)
            other.IsSelected = other == option;
        OnPropertyChanged(nameof(LanguageShort));
        _settings.Settings.Language = option.Language.Code;
        _settings.Save();
        _ = Localisation.SetLanguageAsync(option.Language.Code);
    }

    // ── Undo ───────────────────────────────────────────────────

    // Graphical editors share the workspace history; text tabs have their own undo in the editor.
    public string UndoHeader => Workspace.History.UndoDescription is { } action ? $"Скасувати: {action}" : "Скасувати";

    public string RedoHeader => Workspace.History.RedoDescription is { } action ? $"Повторити: {action}" : "Повторити";

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => StatusMessage = Workspace.Undo();

    private bool CanUndo() => HasActiveEntityTab && Workspace.History.UndoDescription is not null;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => StatusMessage = Workspace.Redo();

    private bool CanRedo() => HasActiveEntityTab && Workspace.History.RedoDescription is not null;

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(UndoHeader));
        OnPropertyChanged(nameof(RedoHeader));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    // ── Renaming ───────────────────────────────────────────────

    [RelayCommand]
    private Task RenameContentNode(ContentNodeViewModel? node) =>
        node is { Kind: { } kind, HasLocation: true } ? RenameAsync(kind, node.Name, true) : Task.CompletedTask;

    [RelayCommand]
    private Task RenameContentNodeDefinition(ContentNodeViewModel? node) =>
        node is { Kind: { } kind, HasLocation: true } ? RenameAsync(kind, node.Name, false) : Task.CompletedTask;

    /// <summary>
    /// Renames an entity. With <paramref name="updateReferences"/> (F2) every whole-word use in
    /// the mod's scripts and its localisation keys change too; without it (Shift+F2) only the
    /// definition does.
    /// </summary>
    public async Task RenameAsync(ContentKind kind, string id, bool updateReferences)
    {
        if (!Renaming.CanRename(kind))
        {
            StatusMessage = "Області не перейменовуються: їхній id — номер, який гра використовує всюди.";
            return;
        }
        var definitionFile = Content.Files
            .FirstOrDefault(f => ContentParser.Flatten(f.Value).Any(e => e.Kind == kind && e.Id == id)).Key;
        if (definitionFile is null)
        {
            StatusMessage = $"«{id}» не знайдено в моді.";
            return;
        }

        var taken = Content.Files.Values.SelectMany(ContentParser.Flatten)
            .Where(e => e.Kind == kind)
            .Select(e => e.Id)
            .ToHashSet(StringComparer.Ordinal);
        var label = updateReferences
            ? $"Нове ім'я для «{id}». Посилання в усіх скриптах і ключі локалізації буде оновлено."
            : $"Нове ім'я для «{id}». Змінюється лише визначення, посилання лишаються як є.";
        var newId = await _platform.PromptAsync(
            updateReferences ? "Перейменування (F2)" : "Перейменування визначення (Shift+F2)",
            label, "Перейменувати", null,
            value => !Renaming.IsValidId(value) ? "Ім'я має бути одним словом, без пробілів, лапок і дужок."
                : value == id ? "Ім'я не змінилося."
                : taken.Contains(value) ? "Таке ім'я вже є." : null,
            id);
        if (newId is null)
            return;

        string? error = null;
        using var action = Workspace.Action($"Перейменування «{id}» → «{newId}»");
        if (updateReferences)
        {
            var plan = PlanRename(id, newId);
            var count = plan.Sum(p => p.Count);
            if (!await _platform.ConfirmAsync("Перейменування",
                    $"«{id}» → «{newId}»: {Ukrainian.Count(count, "заміна", "заміни", "замін")} у {Ukrainian.Count(plan.Count, "файлі", "файлах", "файлах")}. Продовжити?",
                    "Перейменувати", destructive: false))
                return;
            foreach (var (file, _) in plan)
                error ??= Workspace.Edit(file, (text, tree) => RenameEdits(file, text, tree, id, newId));
        }
        else
        {
            var relative = Workspace.Relative(definitionFile);
            error = Workspace.Edit(definitionFile, (_, tree) =>
                Renaming.RenameDefinition(tree, relative, kind, id, newId) is { } edit ? [edit] : []);
        }

        foreach (var tab in Tabs.OfType<EntityTabViewModel>())
            tab.OnRenamed(kind, id, newId);
        // The status bar shows problems; a successful rename speaks for itself in the editors.
        StatusMessage = error;
    }

    /// <summary>Files that mention <paramref name="id"/> and how many replacements each needs.</summary>
    private List<(string File, int Count)> PlanRename(string id, string newId)
    {
        var plan = new List<(string, int)>();
        foreach (var file in EnumerateRenameCandidates())
        {
            if (Workspace.ReadText(file) is not { } text || !text.Contains(id, StringComparison.Ordinal))
                continue;
            var count = RenameEdits(file, text, Clausewitz.SyntaxTree.Parse(text), id, newId).Count();
            if (count > 0)
                plan.Add((file, count));
        }
        return plan;
    }

    private static IEnumerable<Clausewitz.Editing.TextEdit> RenameEdits(string file, string text, Clausewitz.SyntaxTree tree, string id, string newId) =>
        Path.GetExtension(file).Equals(".yml", StringComparison.OrdinalIgnoreCase)
            ? Renaming.RenameLocalisationKeys(text, id, newId)
            : Renaming.RenameReferences(tree, id, newId);

    /// <summary>Every script and localisation file of the mod.</summary>
    private IEnumerable<string> EnumerateRenameCandidates()
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(Mod.RootPath, "*", options)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
            .Where(f => FileKinds.IsScript(f) || Path.GetExtension(f).Equals(".yml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).Equals(ModProject.DescriptorFileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Keys and values learned from the game and the mod, for completion. Null while indexing.</summary>
    [ObservableProperty]
    public partial ScriptVocabulary? Vocabulary { get; set; }

    /// <summary>Sprites and textures of the game with this mod loaded.</summary>
    public GameAssets Assets { get; }

    /// <summary>Focus trees, events, ideas… defined by the mod, for the "Контент" view.</summary>
    public ContentIndex Content { get; }

    /// <summary>Asks the view to put the caret at an offset of a text tab once it is shown.</summary>
    public event Action<EditorTabViewModel, int>? NavigationRequested;

    /// <summary>Long-running background work shown in the status bar, e.g. indexing.</summary>
    [ObservableProperty]
    public partial string? BackgroundActivity { get; set; }

    public ModProject Mod { get; }
    public ProjectTreeViewModel ProjectTree { get; }
    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(HasActiveTab), nameof(ActiveTextTab), nameof(ActiveImageTab),
        nameof(HasActiveTextTab), nameof(HasActiveImageTab), nameof(ActiveEntityTab), nameof(HasActiveEntityTab))]
    public partial DocumentTabViewModel? ActiveTab { get; set; }

    partial void OnActiveTabChanged(DocumentTabViewModel? oldValue, DocumentTabViewModel? newValue)
    {
        OnHistoryChanged();
        if (oldValue is EntityTabViewModel oldEntity)
            oldEntity.IsActive = false;
        if (newValue is EntityTabViewModel newEntity)
            newEntity.IsActive = true;
    }

    public EntityTabViewModel? ActiveEntityTab => ActiveTab as EntityTabViewModel;

    public bool HasActiveEntityTab => ActiveTab is EntityTabViewModel;

    public EditorTabViewModel? ActiveTextTab => ActiveTab as EditorTabViewModel;

    public ImageTabViewModel? ActiveImageTab => ActiveTab as ImageTabViewModel;

    public bool HasActiveTab => ActiveTab is not null;

    public bool HasActiveTextTab => ActiveTab is EditorTabViewModel;

    public bool HasActiveImageTab => ActiveTab is ImageTabViewModel;

    public string Title => ActiveTab is null
        ? $"{Mod.Name} – HoI4 Mod Builder"
        : $"{Mod.Name} – {ActiveTab.Name}";

    [ObservableProperty]
    public partial bool IsProjectPanelVisible { get; set; } = true;

    /// <summary>Transient message (errors etc.) shown in the status bar.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    // ── File menu ──────────────────────────────────────────────

    [RelayCommand]
    private void NewMod() => _host.ShowWelcome(WelcomePage.NewMod);

    [RelayCommand]
    private async Task OpenMod()
    {
        var start = _settings.Settings.ModsDirectory ?? GameLocator.DefaultModsDirectory();
        if (await _platform.PickFolderAsync("Оберіть папку моду", start) is not { } path)
            return;
        try
        {
            _host.OpenMod(ModProject.Load(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidModException)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void CloseMod() => _host.ShowWelcome();

    [RelayCommand]
    private Task OpenSettings() => _platform.ShowSettingsAsync();

    [RelayCommand]
    private void Exit() => _host.Exit();

    // ── Workspace ──────────────────────────────────────────────

    [RelayCommand]
    private void ToggleProjectPanel() => IsProjectPanelVisible = !IsProjectPanelVisible;

    /// <summary>Changes are saved automatically; this only skips the short delay.</summary>
    [RelayCommand]
    private void SaveAll()
    {
        foreach (var tab in Tabs)
            tab.Flush();
    }

    [RelayCommand]
    private void OpenNode(FileNodeViewModel? node)
    {
        if (node is null)
            return;
        if (node.IsDirectory)
            node.IsExpanded = !node.IsExpanded;
        else
            OpenFile(node.FullPath);
    }

    /// <summary>Opens the entity in its graphical editor; folders and categories expand instead.</summary>
    [RelayCommand]
    private void OpenContentNode(ContentNodeViewModel? node)
    {
        if (node is null)
            return;
        if (node is { Kind: { } kind, FilePath: { } path })
            OpenEntity(kind, node.Name, path, node.OwnerId);
        else
            node.IsExpanded = !node.IsExpanded;
    }

    /// <summary>Jumps to where the entity is defined in the script.</summary>
    [RelayCommand]
    private void GoToContentDefinition(ContentNodeViewModel? node)
    {
        if (node?.FilePath is { } path)
            OpenFile(path, node.Position);
    }

    /// <summary>
    /// Opens the graphical editor of an entity: focus trees (with the focus selected, for a
    /// focus), event graphs, or a card for everything else.
    /// </summary>
    public void OpenEntity(ContentKind kind, string id, string? filePath = null, string? ownerTree = null)
    {
        filePath ??= Content.Files
            .Where(f => ContentParser.Flatten(f.Value).Any(e => e.Kind == kind && e.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Select(f => f.Key)
            .FirstOrDefault();
        if (filePath is null)
        {
            StatusMessage = $"«{id}» не знайдено в моді.";
            return;
        }

        if (kind == ContentKind.Focus)
        {
            // A focus is shown in its tree; a shared focus that no tree owns opens as a card.
            ownerTree ??= Content.Files
                .SelectMany(f => f.Value.Where(e => e.Kind == ContentKind.FocusTree && e.Children.Any(c => c.Id == id)))
                .Select(e => e.Id)
                .FirstOrDefault();
            if (ownerTree is not null
                && Content.Files.FirstOrDefault(f => f.Value.Any(e => e.Kind == ContentKind.FocusTree && e.Id == ownerTree)).Key is { } treeFile)
            {
                var tree = (FocusTreeTabViewModel)ActivateEntity(ContentKind.FocusTree, ownerTree,
                    () => new FocusTreeTabViewModel(_entityContext, ownerTree, treeFile, CloseTab));
                tree.Select(id);
                return;
            }
        }

        ActivateEntity(kind, id, () => kind switch
        {
            ContentKind.FocusTree => new FocusTreeTabViewModel(_entityContext, id, filePath, CloseTab),
            ContentKind.Event => new EventGraphTabViewModel(_entityContext, id, filePath, CloseTab),
            _ => new EntityCardTabViewModel(_entityContext, kind, id, filePath, CloseTab),
        });
    }

    private EntityTabViewModel ActivateEntity(ContentKind kind, string id, Func<EntityTabViewModel> create)
    {
        var key = $"{kind}:{id}";
        if (Tabs.OfType<EntityTabViewModel>().FirstOrDefault(t => t.Key == key) is { } existing)
        {
            ActiveTab = existing;
            return existing;
        }
        var tab = create();
        Activate(tab);
        return tab;
    }

    [RelayCommand]
    private void RevealContentNode(ContentNodeViewModel? node)
    {
        if (node?.FilePath is { } path)
            _platform.RevealInFileManager(path);
    }

    [RelayCommand]
    private void RevealNode(FileNodeViewModel? node)
    {
        if (node is not null)
            _platform.RevealInFileManager(node.FullPath);
    }

    [RelayCommand]
    private async Task NewFile(FileNodeViewModel? node)
    {
        var folder = TargetFolder(node);
        var name = await _platform.PromptAsync(
            "Новий файл", $"Новий файл у {DisplayFolder(folder)}", "Створити", "назва.txt або events/назва.txt",
            n => ModFileOperations.ValidateNewName(folder, n));
        if (name is null)
            return;

        try
        {
            var path = ModFileOperations.CreateFile(folder, name);
            ExpandTo(node);
            OpenFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Не вдалося створити файл: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task NewFolder(FileNodeViewModel? node)
    {
        var folder = TargetFolder(node);
        var name = await _platform.PromptAsync(
            "Нова папка", $"Нова папка у {DisplayFolder(folder)}", "Створити", "назва або common/назва",
            n => ModFileOperations.ValidateNewName(folder, n));
        if (name is null)
            return;

        try
        {
            ModFileOperations.CreateFolder(folder, name);
            ExpandTo(node);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Не вдалося створити папку: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteNode))]
    private async Task DeleteNode(FileNodeViewModel? node)
    {
        if (node is null || !CanDeleteNode(node))
            return;

        var where = ModFileOperations.UsesRecycleBin ? "до кошика" : "назавжди";
        var message = node.IsDirectory
            ? $"Видалити папку «{node.Name}» і весь її вміст ({Ukrainian.Count(ModFileOperations.CountFiles(node.FullPath), "файл", "файли", "файлів")})? Її буде переміщено {where}."
            : $"Видалити файл «{node.Name}»? Його буде переміщено {where}.";
        if (!await _platform.ConfirmAsync("Видалення", message, "Видалити", destructive: true))
            return;

        // Close affected tabs first: a pending autosave must not recreate the file.
        foreach (var tab in Tabs.Where(t => Paths.IsSameOrUnder(t.FilePath, node.FullPath)).ToList())
            RemoveTab(tab, discard: true);

        try
        {
            ModFileOperations.Delete(node.FullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Не вдалося видалити «{node.Name}»: {ex.Message}";
        }
    }

    /// <summary>The mod folder itself can't be deleted from inside the workspace.</summary>
    private bool CanDeleteNode(FileNodeViewModel? node) =>
        node is not null && !Paths.AreEqual(node.FullPath, Mod.RootPath);

    /// <summary>New items go into the selected folder, or next to the selected file.</summary>
    private string TargetFolder(FileNodeViewModel? node) => node switch
    {
        null => Mod.RootPath,
        { IsDirectory: true } => node.FullPath,
        _ => Path.GetDirectoryName(node.FullPath) ?? Mod.RootPath,
    };

    private string DisplayFolder(string folder)
    {
        var relative = Path.GetRelativePath(Mod.RootPath, folder);
        return relative == "." ? "корені мода" : relative.Replace('\\', '/');
    }

    private static void ExpandTo(FileNodeViewModel? node)
    {
        if (node is { IsDirectory: true })
            node.IsExpanded = true;
    }

    [RelayCommand]
    private void CloseActiveTab()
    {
        if (ActiveTab is not null)
            CloseTab(ActiveTab);
    }

    /// <param name="offset">Where to put the caret in a text file; null keeps the last position.</param>
    public void OpenFile(string path, int? offset = null)
    {
        if (FindTab(path) is { } existing)
        {
            ActiveTab = existing;
            Navigate(existing, offset);
            return;
        }

        if (TextureDecoder.IsImage(path))
        {
            Activate(new ImageTabViewModel(path, Mod.RootPath, CloseTab, Assets.Textures));
            return;
        }

        if (!TextFileCodec.IsProbablyText(path))
        {
            StatusMessage = $"«{Path.GetFileName(path)}» не є текстовим файлом — переглядач для нього з'явиться пізніше.";
            return;
        }

        try
        {
            var tab = EditorTabViewModel.Open(path, Mod.RootPath, CloseTab);
            // Graphical editors of this file follow the typing.
            tab.Document.TextChanged += (_, _) => Workspace.NotifyChanged(tab.FilePath);
            Activate(tab);
            Navigate(tab, offset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusMessage = $"Не вдалося відкрити «{Path.GetFileName(path)}»: {ex.Message}";
        }
    }

    public void CloseTab(DocumentTabViewModel tab) => RemoveTab(tab, discard: false);

    private void Navigate(DocumentTabViewModel tab, int? offset)
    {
        if (offset is not { } value || tab is not EditorTabViewModel textTab)
            return;
        textTab.SavedCaretOffset = value;
        NavigationRequested?.Invoke(textTab, value);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _watcher?.Dispose();
        foreach (var tab in Tabs)
            tab.Dispose();
    }

    private void Activate(DocumentTabViewModel tab)
    {
        Tabs.Add(tab);
        ActiveTab = tab;
        StatusMessage = null;
    }

    private async Task LoadVocabularyAsync()
    {
        BackgroundActivity = "Індексація скриптів для автодоповнення…";
        try
        {
            Vocabulary = await VocabularyService.LoadAsync(_settings.Settings.GamePath, Mod.RootPath);
        }
        catch (Exception ex)
        {
            // Completion still works with words from the current file; don't take the editor down.
            StatusMessage = $"Не вдалося проіндексувати скрипти: {ex.Message}";
        }
        finally
        {
            BackgroundActivity = null;
        }
    }

    private void RemoveTab(DocumentTabViewModel tab, bool discard)
    {
        if (discard)
            tab.Discard();
        else
            tab.Dispose();

        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;
        Tabs.RemoveAt(index);
        if (ActiveTab == tab)
            ActiveTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    /// <summary>The text or image tab of a file; graphical editors are found by entity instead.</summary>
    private DocumentTabViewModel? FindTab(string path) =>
        Tabs.FirstOrDefault(t => t is not EntityTabViewModel && Paths.AreEqual(t.FilePath, path));

    // ── File system events ─────────────────────────────────────

    private void OnFileChanged(string path)
    {
        Assets.OnFileChanged(path);
        Content.OnPathChanged(path);
        Localisation.OnFileChanged(path);
        Workspace.NotifyChanged(path);
        FindTab(path)?.ReloadFromDisk();
    }

    private void OnEntryCreated(string path)
    {
        ProjectTree.OnEntryChanged(path);
        Assets.OnFileChanged(path);
        Content.OnPathChanged(path);
        Localisation.OnFileChanged(path);
        Workspace.NotifyChanged(path);
        FindTab(path)?.ReloadFromDisk();
    }

    private void OnEntryDeleted(string path)
    {
        ProjectTree.OnEntryChanged(path);
        Assets.OnFileChanged(path);
        Content.OnPathChanged(path);
        Localisation.OnFileChanged(path);
        Workspace.NotifyChanged(path);
        var affected = Tabs.Where(t => Paths.IsSameOrUnder(t.FilePath, path)).ToList();
        if (affected.Count == 0)
            return;

        DispatcherTimer.RunOnce(() =>
        {
            foreach (var tab in affected.Where(t => Tabs.Contains(t)))
            {
                if (File.Exists(tab.FilePath))
                    tab.ReloadFromDisk();
                else
                    RemoveTab(tab, discard: true);
            }
        }, DeleteGracePeriod);
    }

    private void OnEntryRenamed(string oldPath, string newPath)
    {
        ProjectTree.OnEntryChanged(oldPath);
        ProjectTree.OnEntryChanged(newPath);
        Assets.OnFileChanged(oldPath);
        Assets.OnFileChanged(newPath);
        Content.OnPathChanged(oldPath);
        Content.OnPathChanged(newPath);
        Localisation.OnFileChanged(oldPath);
        Localisation.OnFileChanged(newPath);
        Workspace.NotifyChanged(oldPath);
        Workspace.NotifyChanged(newPath);

        foreach (var tab in Tabs.Where(t => Paths.IsSameOrUnder(t.FilePath, oldPath)))
            tab.FilePath = Paths.Rebase(tab.FilePath, oldPath, newPath);
        if (ActiveTab is not null)
            OnPropertyChanged(nameof(Title));
    }

    private void OnWatcherOverflow()
    {
        ProjectTree.ResyncAll();
        _ = Assets.LoadSpritesAsync();
        Content.Reload();
        _ = Localisation.LoadAsync();
        foreach (var tab in Tabs)
            tab.ReloadFromDisk();
    }
}
