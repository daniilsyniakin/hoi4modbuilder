using System.Collections.Concurrent;
using Avalonia.Threading;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Game.Content;

namespace HoI4ModBuilder.Services;

/// <summary>
/// Entities defined by the mod (focus trees, events, ideas…), per file. Built in the background
/// when the mod opens and kept in step with the disk as files change.
/// </summary>
public sealed class ContentIndex
{
    // Editors autosave in bursts; batch the resulting watcher events.
    private static readonly TimeSpan UpdateDelay = TimeSpan.FromMilliseconds(300);

    private readonly string _rootPath;
    private readonly Dictionary<string, IReadOnlyList<ContentEntry>> _files = new(Paths.Comparer);
    private readonly HashSet<string> _dirty = new(Paths.Comparer);
    private readonly DispatcherTimer _updateTimer;
    private bool _busy;
    private bool _reloadRequested;

    public ContentIndex(string rootPath)
    {
        _rootPath = rootPath;
        _updateTimer = new DispatcherTimer { Interval = UpdateDelay };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Stop();
            await UpdateAsync();
        };
    }

    /// <summary>False until the first scan finishes.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Entities by absolute file path; only files that define something are listed.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ContentEntry>> Files => _files;

    /// <summary>Raised on the UI thread after <see cref="Files"/> changed.</summary>
    public event EventHandler? Changed;

    public async Task LoadAsync()
    {
        _busy = true;
        try
        {
            var parsed = await Task.Run(() => ParseAll(EnumerateContentFiles(_rootPath)));
            _files.Clear();
            foreach (var (path, entries) in parsed)
            {
                if (entries is { Count: > 0 })
                    _files[path] = entries;
            }
            IsLoaded = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _busy = false;
        }
        if (_dirty.Count > 0)
            _updateTimer.Start();
    }

    /// <summary>
    /// Called for every watcher event. <paramref name="path"/> may be a file or a whole folder
    /// that was created, deleted or renamed.
    /// </summary>
    public void OnPathChanged(string path)
    {
        _dirty.Add(Paths.Normalize(path));
        _updateTimer.Stop();
        _updateTimer.Start();
    }

    /// <summary>Events were lost; rescan everything.</summary>
    public void Reload()
    {
        _reloadRequested = true;
        _updateTimer.Stop();
        _updateTimer.Start();
    }

    private async Task UpdateAsync()
    {
        if (_busy)
        {
            _updateTimer.Start();
            return;
        }
        if (_reloadRequested)
        {
            _reloadRequested = false;
            _dirty.Clear();
            await LoadAsync();
            return;
        }
        if (!IsLoaded || _dirty.Count == 0)
            return;

        var changed = _dirty.ToList();
        _dirty.Clear();
        // Everything known under a changed path, plus whatever exists there now.
        var targets = _files.Keys
            .Where(file => changed.Any(path => Paths.IsSameOrUnder(file, path)))
            .ToHashSet(Paths.Comparer);

        _busy = true;
        try
        {
            var parsed = await Task.Run(() =>
            {
                foreach (var path in changed)
                {
                    if (Directory.Exists(path))
                        targets.UnionWith(Directory.EnumerateFiles(path, "*.txt", SearchOption.AllDirectories).Where(IsContentFile));
                    else if (IsContentFile(path))
                        targets.Add(path);
                }
                return ParseAll(targets);
            });

            foreach (var (path, entries) in parsed)
            {
                if (entries is null)
                    continue; // Unreadable for now (probably still being written); the next event retries.
                if (entries.Count > 0)
                    _files[path] = entries;
                else
                    _files.Remove(path);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _busy = false;
        }
    }

    private bool IsContentFile(string path) =>
        Paths.IsSameOrUnder(path, _rootPath) && ContentParser.IsContentFile(Path.GetRelativePath(_rootPath, path));

    private static IEnumerable<string> EnumerateContentFiles(string rootPath)
    {
        foreach (var folder in ContentParser.Folders)
        {
            // Folders are written with '/'; normalise so keys match the watcher's paths.
            var directory = Paths.Normalize(Path.Combine(rootPath, folder));
            if (!Directory.Exists(directory))
                continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.txt").ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var file in files)
                yield return file;
        }
    }

    /// <summary>Parses files in parallel. A deleted file yields an empty list, an unreadable one null.</summary>
    private List<(string Path, IReadOnlyList<ContentEntry>? Entries)> ParseAll(IEnumerable<string> files)
    {
        var results = new ConcurrentBag<(string, IReadOnlyList<ContentEntry>?)>();
        Parallel.ForEach(files, file => results.Add((file, ParseFile(file))));
        return results.ToList();
    }

    private IReadOnlyList<ContentEntry>? ParseFile(string path)
    {
        if (!File.Exists(path) || !IsContentFile(path))
            return [];
        try
        {
            // Decoded exactly like the editor does, so positions match its offsets.
            var text = TextFileCodec.Decode(File.ReadAllBytes(path)).Text;
            return ContentParser.Parse(SyntaxTree.Parse(text), Path.GetRelativePath(_rootPath, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }
}
