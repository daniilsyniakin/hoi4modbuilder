using System.Text;

namespace HoI4ModBuilder.Services;

/// <summary>Names derived from the mod for files it creates (localisation, new scripts, sprites).</summary>
public static class ModNames
{
    /// <summary>
    /// The mod's name — or, when that has no Latin letters (e.g. it is Cyrillic), its folder's
    /// name — as a lower-case word: <c>Ukraine Rising</c> → <c>ukraine_rising</c>.
    /// </summary>
    public static string Slug(string modName, string rootPath) =>
        SlugOf(modName) ?? SlugOf(Path.GetFileName(rootPath)) ?? "mod";

    private static string? SlugOf(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
            builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        var slug = builder.ToString().Trim('_');
        return slug.Length == 0 ? null : slug;
    }
}
