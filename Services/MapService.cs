using System.Diagnostics;
using Avalonia.Threading;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Game.Content;
using HoI4ModBuilder.Game.FileSystem;
using HoI4ModBuilder.Game.Map;

namespace HoI4ModBuilder.Services;

/// <summary>A place where the mod does something with a state.</summary>
public sealed record ModStateReference(string File, StateReference Reference);

/// <summary>
/// The map of the game with the mod over it: states, countries and strategic regions (read at
/// start, the checks need them), the province bitmap (read when the map is first shown), and what
/// the mod's scripts do with each state.
/// </summary>
public sealed class MapService
{
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(500);

    private readonly GameFileSystem _fileSystem;
    private readonly ScriptWorkspace _workspace;
    private readonly ContentIndex _content;
    private readonly DispatcherTimer _dataTimer;
    private readonly DispatcherTimer _referencesTimer;
    private Task? _loadingData;
    private Task? _loadingProvinces;
    private bool _provincesStale;

    public MapService(GameFileSystem fileSystem, ScriptWorkspace workspace, ContentIndex content)
    {
        _fileSystem = fileSystem;
        _workspace = workspace;
        _content = content;
        _dataTimer = new DispatcherTimer { Interval = ReloadDelay };
        _dataTimer.Tick += async (_, _) =>
        {
            _dataTimer.Stop();
            await LoadDataAsync();
            if (_provincesStale)
            {
                _provincesStale = false;
                _loadingProvinces = LoadProvincesAsync();
                await _loadingProvinces;
            }
        };
        _referencesTimer = new DispatcherTimer { Interval = ReloadDelay };
        _referencesTimer.Tick += async (_, _) =>
        {
            _referencesTimer.Stop();
            await IndexReferencesAsync();
        };
        workspace.Changed += OnFileChanged;
        content.Changed += (_, _) => ScheduleReferences();
    }

    public MapData Data { get; private set; } = MapData.Empty;

    public bool IsDataLoaded { get; private set; }

    /// <summary>Null until the map is first shown (and when the bitmap can't be read).</summary>
    public ProvinceMap? Provinces { get; private set; }

    /// <summary>Why the map couldn't be drawn, if it couldn't.</summary>
    public string? ProvincesError { get; private set; }

    /// <summary>What the mod does with each state, by state id.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<ModStateReference>> References { get; private set; } =
        new Dictionary<int, IReadOnlyList<ModStateReference>>();

    /// <summary>Raised on the UI thread when states, the bitmap or the references changed.</summary>
    public event EventHandler? Changed;

    public Task EnsureDataAsync() => _loadingData ??= LoadDataAsync();

    /// <summary>Reads the bitmap (a few seconds) unless done already.</summary>
    public Task EnsureProvincesAsync()
    {
        _loadingProvinces ??= LoadProvincesAsync();
        if (References.Count == 0)
            ScheduleReferences();
        return _loadingProvinces;
    }

    public bool CountryExists(string tag) =>
        Data.Tags.Contains(tag)
        // Dynamic countries (civil wars, releasables) are made at runtime.
        || tag.Length == 3 && tag[0] == 'D' && char.IsAsciiDigit(tag[1]) && char.IsAsciiDigit(tag[2]);

    private async Task LoadDataAsync()
    {
        var open = ReadOpen("history/states", "map/strategicregions", "common/country_tags", "common/countries");
        try
        {
            Data = await Task.Run(() => MapData.Load(_fileSystem, path => open.GetValueOrDefault(Paths.Normalize(path))));
            IsDataLoaded = true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Map data failed to load: {ex}");
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadProvincesAsync()
    {
        var bitmap = _fileSystem.Resolve("map/provinces.bmp");
        var definition = _fileSystem.Resolve("map/definition.csv");
        if (bitmap is null || definition is null)
        {
            ProvincesError = "Не знайдено map/provinces.bmp чи map/definition.csv — перевірте шлях до гри в налаштуваннях.";
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        try
        {
            Provinces = await Task.Run(() => ProvinceMap.Load(bitmap.FullPath, definition.FullPath));
            ProvincesError = null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ProvincesError = $"Карту не вдалося прочитати: {ex.Message}";
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleReferences()
    {
        _referencesTimer.Stop();
        _referencesTimer.Start();
    }

    /// <summary>Finds what every script of the mod does with states.</summary>
    private async Task IndexReferencesAsync()
    {
        if (!_content.IsLoaded)
        {
            ScheduleReferences();
            return;
        }
        var root = _workspace.RootPath;
        var texts = _content.Files.Keys
            .Select(file => (File: file, Text: _workspace.ReadText(file)))
            .Where(f => f.Text is not null)
            .ToList();
        References = await Task.Run(() =>
        {
            var all = new List<ModStateReference>();
            foreach (var (file, text) in texts)
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.StartsWith("history/states/", StringComparison.OrdinalIgnoreCase))
                    continue;
                all.AddRange(StateReferences.Find(SyntaxTree.Parse(text!), relative).Select(r => new ModStateReference(file, r)));
            }
            return (IReadOnlyDictionary<int, IReadOnlyList<ModStateReference>>)all
                .GroupBy(r => r.Reference.State)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<ModStateReference>)g.ToList());
        });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Texts of the mod's files under these folders that are open in editors.</summary>
    private Dictionary<string, string> ReadOpen(params string[] folders)
    {
        var open = new Dictionary<string, string>(Paths.Comparer);
        foreach (var folder in folders)
        {
            var directory = Path.Combine(_workspace.RootPath, folder);
            if (!Directory.Exists(directory))
                continue;
            foreach (var path in Directory.EnumerateFiles(directory, "*.txt", SearchOption.AllDirectories))
            {
                if (_workspace.ReadText(path) is { } text)
                    open[Paths.Normalize(path)] = text;
            }
        }
        return open;
    }

    private void OnFileChanged(string path)
    {
        var relative = _workspace.Relative(path);
        if (relative.StartsWith("history/states", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("map/strategicregions", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("common/country_tags", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("common/countries", StringComparison.OrdinalIgnoreCase))
            ScheduleData();
        else if (relative.Equals("map/provinces.bmp", StringComparison.OrdinalIgnoreCase)
                 || relative.Equals("map/definition.csv", StringComparison.OrdinalIgnoreCase))
        {
            if (Provinces is not null)
                _provincesStale = true;
            ScheduleData();
        }
        ScheduleReferences();
    }

    private void ScheduleData()
    {
        if (!IsDataLoaded)
            return;
        _dataTimer.Stop();
        _dataTimer.Start();
    }
}
