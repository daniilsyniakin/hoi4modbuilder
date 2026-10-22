using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Models;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels;

/// <summary>An open file in the editor area, whatever kind of view shows it.</summary>
public abstract partial class DocumentTabViewModel : ViewModelBase, IDisposable
{
    private readonly string _rootPath;
    private readonly Action<DocumentTabViewModel> _close;

    protected DocumentTabViewModel(string filePath, string rootPath, Action<DocumentTabViewModel> close)
    {
        _rootPath = rootPath;
        _close = close;
        FilePath = filePath;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Kind), nameof(DisplayPath), nameof(Breadcrumbs), nameof(Folder))]
    public partial string FilePath { get; set; }

    public virtual string Name => Path.GetFileName(FilePath);

    public FileKind Kind => FileKinds.FromPath(FilePath);

    /// <summary>Set for graphical editors of a game entity; the tab then shows that entity's icon.</summary>
    public virtual ContentKind? EntityKind => null;

    public bool IsEntity => EntityKind is not null;

    public string DisplayPath => Paths.ForDisplay(FilePath);

    /// <summary>Folder relative to the mod root, e.g. <c>common/national_focus</c>.</summary>
    public string Folder => Path.GetDirectoryName(Path.GetRelativePath(_rootPath, FilePath)) ?? "";

    public virtual string Breadcrumbs =>
        string.Join("  ›  ", Path.GetRelativePath(Path.GetDirectoryName(_rootPath)!, FilePath)
            .Split(Path.DirectorySeparatorChar));

    /// <summary>Shown in red in the tab strip when the file couldn't be written.</summary>
    public virtual bool HasSaveError => false;

    [RelayCommand]
    private void Close() => _close(this);

    /// <summary>Writes pending changes to disk immediately.</summary>
    public virtual void Flush()
    {
    }

    /// <summary>Pulls in a change made to the file by another program.</summary>
    public abstract void ReloadFromDisk();

    /// <summary>Releases resources without writing anything (the file is gone).</summary>
    public virtual void Discard()
    {
    }

    public virtual void Dispose()
    {
        Flush();
        Discard();
    }
}
