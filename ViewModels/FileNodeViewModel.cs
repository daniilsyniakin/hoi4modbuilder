using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HoI4ModBuilder.Models;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels;

/// <summary>
/// A file or folder in the project tree. Folders load their children lazily on first expand
/// and can be re-synchronised with the disk when the watcher reports a change.
/// </summary>
public sealed partial class FileNodeViewModel : ViewModelBase
{
    // Folders that are noise for modders.
    private static readonly HashSet<string> HiddenNames = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", ".idea" };

    private bool _isLoaded;

    public FileNodeViewModel(string fullPath, bool isDirectory, string? displayName = null, string? description = null)
    {
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Name = displayName ?? Path.GetFileName(fullPath);
        Description = description;
        Kind = isDirectory ? FileKind.Folder : FileKinds.FromPath(fullPath);

        // A dummy child makes the tree show an expander before the folder has been read.
        if (isDirectory)
            Children.Add(new FileNodeViewModel("", false));
    }

    public string FullPath { get; }
    public string Name { get; }
    public string? Description { get; }
    public bool IsDirectory { get; }
    public FileKind Kind { get; }
    public ObservableCollection<FileNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_isLoaded)
        {
            _isLoaded = true;
            Children.Clear();
            Resync();
        }
    }

    /// <summary>Brings the children in line with the disk, keeping existing nodes (and their expansion state).</summary>
    public void Resync()
    {
        if (!IsDirectory || !_isLoaded)
            return;

        var entries = ReadEntries();
        var present = entries.Select(e => e.FullName).ToHashSet(Paths.Comparer);

        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (!present.Contains(Children[i].FullPath))
                Children.RemoveAt(i);
        }

        // Both lists use the same ordering, so walking them in parallel finds the gaps.
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (i < Children.Count && string.Equals(Children[i].FullPath, entry.FullName, Paths.Comparison))
                continue;
            Children.Insert(i, new FileNodeViewModel(entry.FullName, entry is DirectoryInfo));
        }
    }

    /// <summary>This node and all loaded folders below it.</summary>
    public IEnumerable<FileNodeViewModel> LoadedDirectories()
    {
        if (!IsDirectory || !_isLoaded)
            yield break;
        yield return this;
        foreach (var child in Children)
        foreach (var dir in child.LoadedDirectories())
            yield return dir;
    }

    /// <summary>Finds an already loaded descendant folder by absolute path.</summary>
    public FileNodeViewModel? FindLoadedDirectory(string path)
    {
        if (!Paths.IsSameOrUnder(path, FullPath))
            return null;
        var node = this;
        var relative = Path.GetRelativePath(FullPath, path);
        if (relative == ".")
            return node._isLoaded ? node : null;

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            if (!node._isLoaded)
                return null;
            node = node.Children.FirstOrDefault(c => c.IsDirectory && string.Equals(c.Name, segment, Paths.Comparison));
            if (node is null)
                return null;
        }
        return node._isLoaded ? node : null;
    }

    private List<FileSystemInfo> ReadEntries()
    {
        try
        {
            return new DirectoryInfo(FullPath).EnumerateFileSystemInfos()
                .Where(e => !HiddenNames.Contains(e.Name))
                .OrderBy(e => e is DirectoryInfo ? 0 : 1)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
