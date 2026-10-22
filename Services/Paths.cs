namespace HoI4ModBuilder.Services;

/// <summary>Path comparisons that respect the platform's file system case sensitivity.</summary>
public static class Paths
{
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static StringComparer Comparer { get; } =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool AreEqual(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), Comparison);

    /// <summary>True if <paramref name="path"/> is <paramref name="directory"/> itself or lies inside it.</summary>
    public static bool IsSameOrUnder(string path, string directory)
    {
        path = Normalize(path);
        directory = Normalize(directory);
        return path.Equals(directory, Comparison)
            || path.StartsWith(directory + Path.DirectorySeparatorChar, Comparison);
    }

    /// <summary>Replaces the <paramref name="oldPrefix"/> directory part of <paramref name="path"/>.</summary>
    public static string Rebase(string path, string oldPrefix, string newPrefix)
    {
        var relative = Path.GetRelativePath(oldPrefix, path);
        return relative == "." ? newPrefix : Path.Join(newPrefix, relative);
    }

    /// <summary>Shortens the user's home folder to <c>~</c>, as JetBrains IDEs do.</summary>
    public static string ForDisplay(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, Comparison) ? "~" + path[home.Length..] : path;
    }
}
