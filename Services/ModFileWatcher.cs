using Avalonia.Threading;

namespace HoI4ModBuilder.Services;

/// <summary>
/// Watches the whole mod folder and re-raises file system events on the UI thread.
/// </summary>
public sealed class ModFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;

    public ModFileWatcher(string rootPath)
    {
        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Changed += (_, e) => Post(() => Changed?.Invoke(e.FullPath));
        _watcher.Created += (_, e) => Post(() => Created?.Invoke(e.FullPath));
        _watcher.Deleted += (_, e) => Post(() => Deleted?.Invoke(e.FullPath));
        _watcher.Renamed += (_, e) => Post(() => Renamed?.Invoke(e.OldFullPath, e.FullPath));
        // Raised when the internal buffer overflows and events were lost.
        _watcher.Error += (_, _) => Post(() => Overflow?.Invoke());
        _watcher.EnableRaisingEvents = true;
    }

    public event Action<string>? Changed;
    public event Action<string>? Created;
    public event Action<string>? Deleted;
    public event Action<string, string>? Renamed;
    public event Action? Overflow;

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);
}
