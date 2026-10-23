using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Models;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels;

public enum ProjectViewMode
{
    /// <summary>Game entities (focus trees, events, ideas…) in virtual folders, wherever their files are.</summary>
    Content,

    /// <summary>Plain file system view of the mod folder.</summary>
    Files,
}

/// <summary>The "Project" tool window on the left.</summary>
public sealed partial class ProjectTreeViewModel : ViewModelBase
{
    private readonly FileNodeViewModel _root;
    private readonly ContentIndex _content;
    private readonly ContentTreeBuilder _contentBuilder;

    public ProjectTreeViewModel(ModProject mod, ContentIndex content)
    {
        _root = new FileNodeViewModel(mod.RootPath, true, mod.Name, Paths.ForDisplay(mod.RootPath));
        Roots = [_root];
        _root.IsExpanded = true;

        _content = content;
        _contentBuilder = new ContentTreeBuilder(mod.RootPath, (kind, id) => _problems?.WorstFor(kind, id),
            entry => _localisation?.TitleOf(entry));
        _content.Changed += (_, _) =>
        {
            RebuildContent();
            OnPropertyChanged(nameof(IsContentLoadingShown));
        };
    }

    private ProblemsService? _problems;

    /// <summary>Colours entities by the problems the mod check finds.</summary>
    public void AttachProblems(ProblemsService problems)
    {
        _problems = problems;
        problems.Changed += (_, _) => RebuildContent();
    }

    private LocalisationService? _localisation;

    /// <summary>Names entities in the chosen localisation language.</summary>
    public void AttachLocalisation(LocalisationService localisation)
    {
        _localisation = localisation;
        localisation.Changed += (_, _) => RebuildContent();
    }

    private void RebuildContent() => ContentNodeViewModel.Sync(ContentRoots, _contentBuilder.Build(_content.Files));

    public ObservableCollection<FileNodeViewModel> Roots { get; }

    /// <summary>Categories of the "Контент" view.</summary>
    public ObservableCollection<ContentNodeViewModel> ContentRoots { get; } = [];

    /// <summary>The content view shows a placeholder until the first scan finishes.</summary>
    public bool IsContentLoadingShown => IsContentMode && !_content.IsLoaded;

    [ObservableProperty]
    public partial FileNodeViewModel? SelectedNode { get; set; }

    [ObservableProperty]
    public partial ContentNodeViewModel? SelectedContentNode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeTitle), nameof(IsFilesMode), nameof(IsContentMode), nameof(ToggleModeTip),
        nameof(IsContentLoadingShown))]
    public partial ProjectViewMode Mode { get; set; } = ProjectViewMode.Files;

    public bool IsFilesMode => Mode == ProjectViewMode.Files;

    public bool IsContentMode => Mode == ProjectViewMode.Content;

    public string ModeTitle => Mode switch
    {
        ProjectViewMode.Content => "Контент",
        _ => "Файли",
    };

    /// <summary>What a click on the mode switch leads to, for its tooltip.</summary>
    public string ToggleModeTip => IsContentMode ? "Показати файли" : "Показати контент (фокуси, події, ідеї…)";

    [RelayCommand]
    private void ToggleMode() => Mode = IsContentMode ? ProjectViewMode.Files : ProjectViewMode.Content;

    [RelayCommand]
    private void CollapseAll()
    {
        if (IsContentMode)
        {
            foreach (var node in ContentRoots.SelectMany(r => r.SelfAndDescendants()))
                node.IsExpanded = false;
            return;
        }
        foreach (var dir in _root.LoadedDirectories().Where(d => d != _root))
            dir.IsExpanded = false;
    }

    /// <summary>Called when an entry was created, deleted or renamed at <paramref name="path"/>.</summary>
    public void OnEntryChanged(string path)
    {
        if (Path.GetDirectoryName(path) is { } parent)
            _root.FindLoadedDirectory(parent)?.Resync();
    }

    public void ResyncAll()
    {
        foreach (var dir in _root.LoadedDirectories().ToList())
            dir.Resync();
    }
}
