using Avalonia.Threading;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.FileSystem;

namespace HoI4ModBuilder.Services;

/// <summary>The technologies of the game and the mod, read in the background and kept up to date with the mod's files.</summary>
public sealed class TechnologyService
{
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(500);

    private readonly GameFileSystem _fileSystem;
    private readonly ScriptWorkspace _workspace;
    private readonly DispatcherTimer _reloadTimer;
    private Task? _loading;

    public TechnologyService(GameFileSystem fileSystem, ScriptWorkspace workspace)
    {
        _fileSystem = fileSystem;
        _workspace = workspace;
        _reloadTimer = new DispatcherTimer { Interval = ReloadDelay };
        _reloadTimer.Tick += async (_, _) =>
        {
            _reloadTimer.Stop();
            await LoadAsync();
        };
        workspace.Changed += OnFileChanged;
    }

    public TechnologyCatalog Catalog { get; private set; } = TechnologyCatalog.Empty;

    public bool IsLoaded { get; private set; }

    /// <summary>Raised on the UI thread whenever <see cref="Catalog"/> is replaced.</summary>
    public event EventHandler? Changed;

    /// <summary>Starts reading unless that has been done already.</summary>
    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        // Open editors may hold newer text than the disk; read them on the UI thread first.
        var modFolder = Path.Combine(_workspace.RootPath, "common", "technologies");
        var open = new Dictionary<string, string>(Paths.Comparer);
        if (Directory.Exists(modFolder))
        {
            foreach (var path in Directory.EnumerateFiles(modFolder, "*.txt", SearchOption.AllDirectories))
            {
                if (_workspace.ReadText(path) is { } text)
                    open[Paths.Normalize(path)] = text;
            }
        }
        Catalog = await Task.Run(() => TechnologyCatalog.Load(_fileSystem, path => open.GetValueOrDefault(Paths.Normalize(path))));
        IsLoaded = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFileChanged(string path)
    {
        if (!IsLoaded || !Paths.IsSameOrUnder(path, Path.Combine(_workspace.RootPath, "common", "technologies"))
            && !Paths.IsSameOrUnder(Path.Combine(_workspace.RootPath, "common", "technologies"), path))
            return;
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }
}
