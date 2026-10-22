using System.Diagnostics;
using System.Text.Json;
using HoI4ModBuilder.Models;

namespace HoI4ModBuilder.Services;

/// <summary>Loads and persists <see cref="AppSettings"/> in the user's application data folder.</summary>
public sealed class SettingsStore
{
    private const int MaxRecentMods = 30;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HoI4ModBuilder",
        "settings.json");

    private SettingsStore(AppSettings settings) => Settings = settings;

    public AppSettings Settings { get; }

    public static SettingsStore Load()
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.WriteLine($"Failed to read settings, using defaults: {ex.Message}");
        }

        var store = new SettingsStore(settings ?? new AppSettings());
        store.FillDefaults();
        return store;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    public void AddRecent(string name, string path)
    {
        var normalized = Paths.Normalize(path);
        Settings.RecentMods.RemoveAll(m => Paths.AreEqual(m.Path, normalized));
        Settings.RecentMods.Insert(0, new RecentMod { Name = name, Path = normalized, LastOpened = DateTime.Now });
        if (Settings.RecentMods.Count > MaxRecentMods)
            Settings.RecentMods.RemoveRange(MaxRecentMods, Settings.RecentMods.Count - MaxRecentMods);
        Save();
    }

    public void RemoveRecent(string path)
    {
        if (Settings.RecentMods.RemoveAll(m => Paths.AreEqual(m.Path, path)) == 0)
            return;
        Save();
    }

    private void FillDefaults()
    {
        var changed = false;
        if (!GameLocator.IsGameDirectory(Settings.GamePath) && GameLocator.FindGamePath() is { } gamePath)
        {
            Settings.GamePath = gamePath;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(Settings.ModsDirectory))
        {
            Settings.ModsDirectory = GameLocator.DefaultModsDirectory();
            changed = true;
        }
        if (changed)
            Save();
    }
}
