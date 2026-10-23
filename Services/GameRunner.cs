using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HoI4ModBuilder.Models;

namespace HoI4ModBuilder.Services;

/// <summary>
/// Starts Hearts of Iron IV with the open mod — and only it — enabled, bypassing the Paradox
/// launcher. The player's own mod list (<c>dlc_load.json</c>) is put back when the game closes.
/// </summary>
public sealed partial class GameRunner(SettingsStore settings, ModProject mod) : ObservableObject
{
    private Process? _process;
    private string? _originalDlcLoad;
    private string? _writtenDlcLoad;

    /// <summary>Documents/Paradox Interactive/Hearts of Iron IV: logs, mod list, launcher files.</summary>
    public string UserDirectory =>
        Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(ModsDirectory)) ?? ModsDirectory;

    public string ErrorLogPath => Path.Combine(UserDirectory, "logs", "error.log");

    private string ModsDirectory => settings.Settings.ModsDirectory ?? GameLocator.DefaultModsDirectory();

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    /// <summary>When the current (or last) game was started; log lines from before belong to another run.</summary>
    public DateTime? StartedAt { get; private set; }

    /// <summary>Raised on the UI thread when the game has closed.</summary>
    public event EventHandler? Exited;

    /// <returns>An error message, or null when the game is starting.</returns>
    public string? Start()
    {
        if (IsRunning)
            return null;
        var gamePath = settings.Settings.GamePath;
        if (!GameLocator.IsGameDirectory(gamePath))
            return "Не вказано папку Hearts of Iron IV (Налаштування).";
        var exe = Path.Combine(gamePath!, OperatingSystem.IsWindows() ? "hoi4.exe" : "hoi4");
        if (!File.Exists(exe))
            return $"Не знайдено {Path.GetFileName(exe)} у папці гри.";

        try
        {
            var launcherFile = EnsureLauncherFile();
            EnableOnly("mod/" + Path.GetFileName(launcherFile));

            // -debug: the game counts errors on screen and logs more.
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(exe, "-debug") { WorkingDirectory = gamePath!, UseShellExecute = false },
                EnableRaisingEvents = true,
            };
            process.Exited += (_, _) => Dispatcher.UIThread.Post(OnExited);
            StartedAt = DateTime.Now;
            process.Start();
            _process = process;
            IsRunning = true;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.ComponentModel.Win32Exception)
        {
            RestoreModList();
            return $"Не вдалося запустити гру: {ex.Message}";
        }
    }

    /// <summary>Closes the game (as the IDE's stop button does: no questions).</summary>
    public void Stop()
    {
        try
        {
            _process?.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    private void OnExited()
    {
        _process?.Dispose();
        _process = null;
        IsRunning = false;
        RestoreModList();
        Exited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The launcher's <c>.mod</c> file pointing at this mod; created when there is none.</summary>
    private string EnsureLauncherFile()
    {
        Directory.CreateDirectory(ModsDirectory);
        foreach (var file in Directory.EnumerateFiles(ModsDirectory, "*.mod"))
        {
            var text = File.ReadAllText(file);
            if (PathLine().Match(text) is { Success: true } match && Paths.AreEqual(match.Groups["path"].Value, mod.RootPath))
                return file;
        }

        var name = Path.GetFileName(mod.RootPath);
        var path = Path.Combine(ModsDirectory, name + ".mod");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(ModsDirectory, $"{name}_{n}.mod");
        File.WriteAllText(path, mod.Descriptor.Serialize(mod.RootPath));
        return path;
    }

    /// <summary>Writes a mod list with just this mod, remembering the player's own to put it back later.</summary>
    private void EnableOnly(string launcherName)
    {
        var path = Path.Combine(UserDirectory, "dlc_load.json");
        _originalDlcLoad = File.Exists(path) ? File.ReadAllText(path) : null;
        var json = (_originalDlcLoad is null ? null : JsonNode.Parse(_originalDlcLoad) as JsonObject) ?? new JsonObject();
        json["enabled_mods"] = new JsonArray(launcherName);
        json["disabled_dlcs"] ??= new JsonArray();
        _writtenDlcLoad = json.ToJsonString();
        File.WriteAllText(path, _writtenDlcLoad);
    }

    /// <summary>Puts the player's mod list back, unless something else changed it meanwhile.</summary>
    private void RestoreModList()
    {
        var path = Path.Combine(UserDirectory, "dlc_load.json");
        try
        {
            if (_writtenDlcLoad is not null && File.Exists(path) && File.ReadAllText(path) == _writtenDlcLoad)
            {
                if (_originalDlcLoad is null)
                    File.Delete(path);
                else
                    File.WriteAllText(path, _originalDlcLoad);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Could not restore the mod list: {ex.Message}");
        }
        _writtenDlcLoad = null;
        _originalDlcLoad = null;
    }

    [GeneratedRegex("""^\s*path\s*=\s*"(?<path>[^"]*)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex PathLine();
}
