using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Completion;
using HoI4ModBuilder.Models;

namespace HoI4ModBuilder.Services;

/// <summary>
/// Builds the completion vocabulary from the game's scripts (cached on disk, since indexing
/// ~100 MB takes a few seconds) plus the mod's own scripts (always fresh).
/// </summary>
public static class VocabularyService
{
    // Text files that happen to have a script extension (credits, licences) produce garbage.
    private const int MaxDiagnosticsPerFile = 10;

    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase) { ".git", ".vs", ".idea" };

    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HoI4ModBuilder", "cache", "vocabulary.json");

    private static readonly Lock GameLock = new();
    private static (string Path, Task<ScriptVocabulary> Task)? _game;

    public static async Task<ScriptVocabulary> LoadAsync(string? gamePath, string modRoot)
    {
        var game = GameLocator.IsGameDirectory(gamePath) ? await GetGameVocabularyAsync(gamePath!) : null;
        return await Task.Run(() =>
        {
            var result = new ScriptVocabulary();
            if (game is not null)
                result.Merge(game);
            result.Merge(Build(modRoot));
            return result;
        });
    }

    /// <summary>
    /// The game's vocabulary alone (cached and shared with <see cref="LoadAsync"/>), for checks
    /// that must not take the mod's own spelling as proof; null without a game folder.
    /// </summary>
    public static async Task<ScriptVocabulary?> LoadGameAsync(string? gamePath) =>
        GameLocator.IsGameDirectory(gamePath) ? await GetGameVocabularyAsync(gamePath!) : null;

    /// <summary>Shared between workspaces: switching mods doesn't re-index the game.</summary>
    private static Task<ScriptVocabulary> GetGameVocabularyAsync(string gamePath)
    {
        lock (GameLock)
        {
            if (_game is { } cached && Paths.AreEqual(cached.Path, gamePath) && !cached.Task.IsFaulted)
                return cached.Task;
            var task = Task.Run(() => LoadOrBuildGameVocabulary(gamePath));
            _game = (gamePath, task);
            return task;
        }
    }

    private static ScriptVocabulary LoadOrBuildGameVocabulary(string gamePath)
    {
        var files = EnumerateScripts(gamePath).ToList();
        var stamp = new CacheStamp(
            ScriptVocabulary.FormatVersion,
            Paths.Normalize(gamePath),
            GameLocator.ReadGameVersion(gamePath)?.ToString() ?? "",
            files.Count);

        if (TryReadCache(stamp) is { } cached)
            return cached;

        var stopwatch = Stopwatch.StartNew();
        var vocabulary = Build(gamePath, files);
        Trace.WriteLine($"Indexed {files.Count} game scripts in {stopwatch.Elapsed.TotalSeconds:F1} s.");
        WriteCache(new VocabularyCache(stamp, vocabulary));
        return vocabulary;
    }

    private static ScriptVocabulary Build(string root, IReadOnlyList<string>? files = null)
    {
        files ??= EnumerateScripts(root).ToList();
        var partials = new ConcurrentBag<ScriptVocabulary>();

        Parallel.ForEach(
            files,
            () => new ScriptVocabulary(),
            (file, _, local) =>
            {
                try
                {
                    var tree = SyntaxTree.Parse(TextFileCodec.Decode(File.ReadAllBytes(file)).Text);
                    if (tree.Diagnostics.Count <= MaxDiagnosticsPerFile)
                        local.AddFile(tree, Path.GetDirectoryName(Path.GetRelativePath(root, file)) ?? "");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    // Unreadable files simply don't contribute.
                }
                return local;
            },
            partials.Add);

        var result = new ScriptVocabulary();
        foreach (var partial in partials)
            result.Merge(partial);
        return result;
    }

    private static IEnumerable<string> EnumerateScripts(string root)
    {
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (!IgnoredFolders.Contains(Path.GetFileName(sub)))
                        pending.Push(sub);
                }
                entries = Directory.EnumerateFiles(dir).Where(FileKinds.IsScript).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var file in entries)
                yield return file;
        }
    }

    private static ScriptVocabulary? TryReadCache(CacheStamp stamp)
    {
        try
        {
            if (!File.Exists(CachePath))
                return null;
            using var stream = File.OpenRead(CachePath);
            var cache = JsonSerializer.Deserialize<VocabularyCache>(stream);
            return cache is not null && cache.Stamp == stamp ? cache.Vocabulary : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void WriteCache(VocabularyCache cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            using var stream = File.Create(CachePath);
            JsonSerializer.Serialize(stream, cache);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Failed to write vocabulary cache: {ex.Message}");
        }
    }

    /// <summary>Identifies what the cache was built from; any difference triggers a rebuild.</summary>
    private sealed record CacheStamp(int Format, string GamePath, string GameVersion, int FileCount);

    private sealed record VocabularyCache(CacheStamp Stamp, ScriptVocabulary Vocabulary);
}
