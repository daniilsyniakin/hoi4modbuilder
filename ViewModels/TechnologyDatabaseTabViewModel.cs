using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels;

/// <summary>A technology in the database: what the research screen would show about it.</summary>
public sealed partial class TechnologyRowViewModel(TechnologyInfo info, string name, string? folder, string categories,
    string? leadsTo, string? requires, Func<string, Task<IImage?>> loadIcon) : ViewModelBase
{
    private bool _iconRequested;

    public TechnologyInfo Info { get; } = info;

    public string Id => Info.Id;

    public string Name { get; } = name;

    public string? Folder { get; } = folder;

    public string Categories { get; } = categories;

    public bool IsMod => Info.IsMod;

    public string YearText => Info.StartYear?.ToString() ?? "";

    /// <summary>Base research time: the cost is in hundreds of days.</summary>
    public string DaysText => Info.ResearchCost is { } cost ? $"{cost * 100:0}" : "";

    public string Details { get; } = string.Join("\n", new[] { leadsTo, requires, info.RelativePath }.Where(s => s is not null));

    /// <summary>The research screen picture (<c>GFX_&lt;id&gt;_medium</c>), loaded when the row is first shown.</summary>
    public IImage? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                _ = LoadIcon();
            }
            return field;
        }
        private set => SetProperty(ref field, value);
    }

    private async Task LoadIcon() => Icon = await loadIcon("GFX_" + Id + "_medium");
}

/// <summary>Every technology of the game and the mod in one list, with filters by origin, folder and text.</summary>
public sealed partial class TechnologyDatabaseTabViewModel : DocumentTabViewModel
{
    public const string AllFolders = "Усі теки";

    private readonly TechnologyService _technologies;
    private readonly LocalisationService _localisation;
    private readonly GameAssets _assets;
    private readonly Action<ContentKind, string> _openEntity;
    private List<TechnologyRowViewModel> _all = [];

    public TechnologyDatabaseTabViewModel(TechnologyService technologies, LocalisationService localisation, GameAssets assets,
        string rootPath, Action<ContentKind, string> openEntity, Action<DocumentTabViewModel> close)
        : base(Path.Combine(rootPath, "common", "technologies"), rootPath, close)
    {
        _technologies = technologies;
        _localisation = localisation;
        _assets = assets;
        _openEntity = openEntity;
        technologies.Changed += OnSourceChanged;
        localisation.Changed += OnSourceChanged;
        _ = technologies.EnsureLoadedAsync();
        Rebuild();
    }

    public override string Name => "Технології";

    public override string Breadcrumbs => "Технології гри й мода";

    public override ContentKind? EntityKind => ContentKind.Technology;

    [ObservableProperty]
    public partial IReadOnlyList<TechnologyRowViewModel> Rows { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> Folders { get; private set; } = [AllFolders];

    [ObservableProperty]
    public partial string SelectedFolder { get; set; } = AllFolders;

    [ObservableProperty]
    public partial string? Search { get; set; }

    [ObservableProperty]
    public partial bool ShowMod { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowGame { get; set; } = true;

    [ObservableProperty]
    public partial int ModCount { get; private set; }

    [ObservableProperty]
    public partial int GameCount { get; private set; }

    public bool IsLoading => !_technologies.IsLoaded;

    public bool IsEmpty => _technologies.IsLoaded && Rows.Count == 0;

    partial void OnSearchChanged(string? value) => Filter();

    partial void OnShowModChanged(bool value) => Filter();

    partial void OnShowGameChanged(bool value) => Filter();

    partial void OnSelectedFolderChanged(string value) => Filter();

    partial void OnRowsChanged(IReadOnlyList<TechnologyRowViewModel> value) => OnPropertyChanged(nameof(IsEmpty));

    /// <summary>A technology of the mod opens in its editor.</summary>
    [RelayCommand]
    private void Open(TechnologyRowViewModel? row)
    {
        if (row is { IsMod: true })
            _openEntity(ContentKind.Technology, row.Id);
    }

    private void OnSourceChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        var catalog = _technologies.Catalog;
        string Text(string key) => _localisation.Text(key) ?? key;
        // As the game names it: its own text, or that of the first equipment it unlocks.
        string Name(TechnologyInfo tech) =>
            _localisation.Text(tech.Id) ?? tech.Unlocks.Select(_localisation.Text).FirstOrDefault(n => n is not null) ?? tech.Id;
        string? Names(IEnumerable<string> ids, string prefix) =>
            ids.Any() ? prefix + string.Join(", ", ids.Select(id => catalog.Find(id) is { } t ? Name(t) : Text(id))) : null;

        _all = catalog.All
            // The mod's version of a technology replaces the game's.
            .GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(t => t.IsMod).First())
            .Select(t => new TechnologyRowViewModel(t, Name(t), t.Folder is null ? null : Text(t.Folder),
                string.Join(", ", t.Categories.Select(Text)),
                Names(t.LeadsTo, "Веде до: "), Names(catalog.Prerequisites(t.Id), "Після: "),
                _assets.LoadPictureAsync))
            .OrderBy(r => r.Info.StartYear ?? int.MaxValue)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        ModCount = _all.Count(r => r.IsMod);
        GameCount = _all.Count - ModCount;
        Folders = [AllFolders, .. _all.Select(r => r.Folder).OfType<string>().Distinct().Order(StringComparer.CurrentCultureIgnoreCase)];
        if (!Folders.Contains(SelectedFolder))
            SelectedFolder = AllFolders;
        OnPropertyChanged(nameof(IsLoading));
        Filter();
    }

    private void Filter()
    {
        var words = (Search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Rows = _all
            .Where(r => r.IsMod ? ShowMod : ShowGame)
            .Where(r => SelectedFolder == AllFolders || r.Folder == SelectedFolder)
            .Where(r => words.All(w => r.Id.Contains(w, StringComparison.OrdinalIgnoreCase)
                                       || r.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                                       || r.Categories.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
    }

    public override void ReloadFromDisk()
    {
    }

    public override void Discard()
    {
        _technologies.Changed -= OnSourceChanged;
        _localisation.Changed -= OnSourceChanged;
    }
}
