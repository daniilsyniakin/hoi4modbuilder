using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HoI4ModBuilder.Services;

/// <summary>Finds the Hearts of Iron IV installation and the launcher's mod directory.</summary>
public static partial class GameLocator
{
    private const string GameFolderName = "Hearts of Iron IV";
    private const string FallbackSupportedVersion = "1.16.*";

    public static string? FindGamePath() =>
        EnumerateSteamLibraries()
            .Select(lib => Path.GetFullPath(Path.Combine(lib, "steamapps", "common", GameFolderName)))
            .FirstOrDefault(IsGameDirectory);

    public static bool IsGameDirectory(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && Directory.Exists(Path.Combine(path, "common"))
        && File.Exists(Path.Combine(path, "launcher-settings.json"));

    /// <summary>Folder where the Paradox launcher looks for <c>*.mod</c> files.</summary>
    public static string DefaultModsDirectory()
    {
        var root = OperatingSystem.IsLinux()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(root, "Paradox Interactive", GameFolderName, "mod");
    }

    /// <summary>Game version as a launcher wildcard, e.g. <c>1.16.*</c>.</summary>
    public static string SupportedVersionFor(string? gamePath) =>
        ReadGameVersion(gamePath) is { } version ? $"{version.Major}.{version.Minor}.*" : FallbackSupportedVersion;

    public static Version? ReadGameVersion(string? gamePath)
    {
        if (!IsGameDirectory(gamePath))
            return null;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(gamePath!, "launcher-settings.json")));
            foreach (var key in new[] { "rawVersion", "version" })
            {
                if (json.RootElement.TryGetProperty(key, out var prop)
                    && prop.ValueKind == JsonValueKind.String
                    && VersionRegex().Match(prop.GetString()!) is { Success: true } match)
                {
                    return Version.Parse(match.Value);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return null;
    }

    private static IEnumerable<string> EnumerateSteamLibraries()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in SteamRoots())
        {
            if (!Directory.Exists(root) || !seen.Add(Path.GetFullPath(root)))
                continue;
            yield return root;

            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;
            string text;
            try { text = File.ReadAllText(vdf); }
            catch (IOException) { continue; }

            foreach (Match match in LibraryPathRegex().Matches(text))
            {
                var library = match.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(library) && seen.Add(Path.GetFullPath(library)))
                    yield return library;
            }
        }
    }

    private static IEnumerable<string> SteamRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steamPath)
                yield return steamPath;
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(home, "Library", "Application Support", "Steam");
        }
        else
        {
            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".local", "share", "Steam");
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPathRegex();

    [GeneratedRegex(@"\d+\.\d+(\.\d+)?")]
    private static partial Regex VersionRegex();
}
