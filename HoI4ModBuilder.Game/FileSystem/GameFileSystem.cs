namespace HoI4ModBuilder.Game.FileSystem;

public enum FileLayer
{
    Game,
    /// <summary>An unpacked DLC under <c>&lt;game&gt;/dlc/&lt;name&gt;/</c>, mounted over vanilla.</summary>
    Dlc,
    Mod,
}

/// <summary>A file as the game sees it: its path relative to the game root and where it really lives.</summary>
public sealed record VirtualFile(string RelativePath, string FullPath, FileLayer Layer);

/// <summary>
/// The game's view of files: the vanilla install, the unpacked DLCs over it and the mod on top.
/// A file in a higher layer replaces one with the same relative path below, exactly as Hearts of
/// Iron IV loads them. Relative paths use forward slashes and are case-insensitive, like the
/// game's own references.
/// </summary>
/// <remarks>DLCs that ship as .zip archives (a handful of unit packs) are not mounted yet.</remarks>
public sealed class GameFileSystem
{
    private readonly (string Root, FileLayer Layer)[] _layers;

    /// <param name="gameRoot">The game install folder; null when the game wasn't found.</param>
    /// <param name="modRoot">The mod folder; null to browse vanilla only.</param>
    public GameFileSystem(string? gameRoot, string? modRoot)
    {
        GameRoot = gameRoot;
        ModRoot = modRoot;

        // Highest priority first.
        var layers = new List<(string, FileLayer)>();
        if (IsDirectory(modRoot))
            layers.Add((Path.GetFullPath(modRoot!), FileLayer.Mod));
        if (IsDirectory(gameRoot))
        {
            var dlcRoot = Path.Combine(gameRoot!, "dlc");
            if (Directory.Exists(dlcRoot))
            {
                layers.AddRange(Directory.EnumerateDirectories(dlcRoot)
                    .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                    .Select(d => (Path.GetFullPath(d), FileLayer.Dlc)));
            }
            layers.Add((Path.GetFullPath(gameRoot!), FileLayer.Game));
        }
        _layers = layers.ToArray();
    }

    private static bool IsDirectory(string? path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    public string? GameRoot { get; }

    public string? ModRoot { get; }

    public static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');

    /// <summary>The file that wins for <paramref name="relativePath"/>, or null if no layer has it.</summary>
    public VirtualFile? Resolve(string relativePath)
    {
        var relative = NormalizeRelative(relativePath);
        foreach (var (root, layer) in _layers)
        {
            var full = Path.Combine(root, relative);
            if (File.Exists(full))
                return new VirtualFile(relative, full, layer);
        }
        return null;
    }

    /// <summary>
    /// Like <see cref="Resolve"/>, but also tries the other texture extensions: scripts often say
    /// <c>.tga</c> where a <c>.dds</c> ships, and the game accepts either.
    /// </summary>
    public VirtualFile? ResolveTexture(string relativePath)
    {
        if (Resolve(relativePath) is { } exact)
            return exact;
        foreach (var extension in new[] { ".dds", ".tga", ".png" })
        {
            if (Resolve(Path.ChangeExtension(relativePath, extension)) is { } alternative)
                return alternative;
        }
        return null;
    }

    /// <summary>Relative path of an absolute path inside one of the layers, or null if it's outside them.</summary>
    public string? ToRelative(string fullPath)
    {
        var full = Path.GetFullPath(fullPath);
        foreach (var (root, _) in _layers)
        {
            var relative = Path.GetRelativePath(root, full);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                return NormalizeRelative(relative);
        }
        return null;
    }

    /// <summary>
    /// Files under <paramref name="folder"/> (recursively) matching <paramref name="pattern"/>, merged
    /// across layers so each relative path appears once, from the winning layer. Sorted by path.
    /// </summary>
    public IReadOnlyList<VirtualFile> EnumerateFiles(string folder, string pattern)
    {
        var files = new Dictionary<string, VirtualFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, layer) in _layers)
        {
            var directory = Path.Combine(root, NormalizeRelative(folder));
            if (!Directory.Exists(directory))
                continue;

            IEnumerable<string> found;
            try
            {
                found = Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var full in found)
            {
                var relative = NormalizeRelative(Path.GetRelativePath(root, full));
                files.TryAdd(relative, new VirtualFile(relative, full, layer)); // higher layers were added first
            }
        }
        return files.Values.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
