using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Models;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels;

public enum WelcomePage
{
    Mods,
    NewMod,
    Settings,
}

public sealed partial class WelcomeViewModel : ViewModelBase
{
    private readonly SettingsStore _store;
    private readonly IPlatformService _platform;
    private readonly IWorkspaceHost _host;
    private List<RecentModItemViewModel> _allMods = [];

    public WelcomeViewModel(
        SettingsStore store,
        IPlatformService platform,
        IWorkspaceHost host,
        WelcomePage initialPage = WelcomePage.Mods)
    {
        _store = store;
        _platform = platform;
        _host = host;
        Settings = new SettingsViewModel(store, platform);
        ReloadRecentMods();

        if (initialPage == WelcomePage.NewMod)
            StartNewMod();
        else
            CurrentPage = initialPage;
    }

    /// <summary>New versions from GitHub releases; offered under the version.</summary>
    public UpdateService Updates { get; init; } = new(null);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task InstallUpdate() => Updates.InstallAsync(() => { });

    public string AppVersion { get; } =
        "версія " + (typeof(WelcomeViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    public SettingsViewModel Settings { get; }

    public ObservableCollection<RecentModItemViewModel> FilteredMods { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavIndex), nameof(IsNewModPage), nameof(IsSettingsPage),
        nameof(ShowRecentList), nameof(ShowEmptyState))]
    public partial WelcomePage CurrentPage { get; set; }

    [ObservableProperty]
    public partial NewModViewModel? NewModForm { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Index in the sidebar; the "new mod" form belongs to the Mods section.</summary>
    public int NavIndex
    {
        get => CurrentPage == WelcomePage.Settings ? 1 : 0;
        set
        {
            var page = value == 1 ? WelcomePage.Settings : WelcomePage.Mods;
            if (page != NavSection(CurrentPage))
                CurrentPage = page;
        }
    }

    public bool IsNewModPage => CurrentPage == WelcomePage.NewMod;
    public bool IsSettingsPage => CurrentPage == WelcomePage.Settings;
    public bool HasRecentMods => _allMods.Count > 0;
    public bool ShowRecentList => CurrentPage == WelcomePage.Mods && HasRecentMods;
    public bool ShowEmptyState => CurrentPage == WelcomePage.Mods && !HasRecentMods;
    public bool NoSearchResults => HasRecentMods && FilteredMods.Count == 0;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void StartNewMod()
    {
        ErrorMessage = null;
        var modsDirectory = _store.Settings.ModsDirectory ?? GameLocator.DefaultModsDirectory();
        NewModForm = new NewModViewModel(
            _platform,
            modsDirectory,
            GameLocator.SupportedVersionFor(_store.Settings.GamePath),
            created: OpenModAt,
            cancelled: () => CurrentPage = WelcomePage.Mods);
        CurrentPage = WelcomePage.NewMod;
    }

    [RelayCommand]
    private async Task OpenMod()
    {
        var start = _store.Settings.ModsDirectory ?? GameLocator.DefaultModsDirectory();
        if (await _platform.PickFolderAsync("Оберіть папку моду", start) is { } path)
            OpenModAt(path);
    }

    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    private void OpenModAt(string path)
    {
        ErrorMessage = null;
        ModProject project;
        try
        {
            project = ModProject.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidModException)
        {
            ErrorMessage = ex.Message;
            return;
        }

        _host.OpenMod(project);
    }

    private void ReloadRecentMods()
    {
        _allMods = _store.Settings.RecentMods
            .Select(m => new RecentModItemViewModel(
                m,
                open: item => OpenModAt(item.Path),
                remove: RemoveRecent,
                reveal: item => _platform.RevealInFileManager(item.Path)))
            .ToList();
        ApplyFilter();
        OnPropertyChanged(nameof(HasRecentMods));
        OnPropertyChanged(nameof(ShowRecentList));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void RemoveRecent(RecentModItemViewModel item)
    {
        _store.RemoveRecent(item.Path);
        ReloadRecentMods();
    }

    private void ApplyFilter()
    {
        var filter = SearchText.Trim();
        FilteredMods.Clear();
        foreach (var mod in _allMods.Where(m => filter.Length == 0 || m.Matches(filter)))
            FilteredMods.Add(mod);
        OnPropertyChanged(nameof(NoSearchResults));
    }

    private static WelcomePage NavSection(WelcomePage page) =>
        page == WelcomePage.NewMod ? WelcomePage.Mods : page;
}
