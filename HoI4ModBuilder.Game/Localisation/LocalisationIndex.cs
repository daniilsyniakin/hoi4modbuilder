using System.Text;
using System.Text.RegularExpressions;
using HoI4ModBuilder.Game.FileSystem;

namespace HoI4ModBuilder.Game.Localisation;

/// <summary>
/// Display texts for localisation keys (focus names, event titles…) in one language, merged the
/// way the game does it: the mod's files override vanilla's. Keys the mod only has in another
/// language are used as a fallback, so a mod written in, say, Russian still shows its names.
/// </summary>
public sealed partial class LocalisationIndex
{
    private readonly Dictionary<string, string> _texts;
    private readonly HashSet<string> _fallbacks;

    private LocalisationIndex(Dictionary<string, string> texts, HashSet<string> fallbacks)
    {
        _texts = texts;
        _fallbacks = fallbacks;
    }

    /// <summary>A new empty index (not shared: <see cref="Set"/> can write to it).</summary>
    public static LocalisationIndex Empty =>
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public int Count => _texts.Count;

    /// <summary>The raw text (with colour codes and <c>$variables$</c>), or null.</summary>
    public string? Find(string? key) => key is not null && _texts.TryGetValue(key, out var text) ? text : null;

    /// <summary>
    /// Takes a just-written text into account right away (the files are re-read shortly after).
    /// <paramref name="value"/> is in the stored form: <c>\n</c> escapes, plain quotes.
    /// </summary>
    public void Set(string key, string value)
    {
        _texts[key] = value;
        _fallbacks.Remove(key);
    }

    /// <summary>Whether the text of <paramref name="key"/> comes from another language (it isn't translated yet).</summary>
    public bool IsFallback(string key) => _fallbacks.Contains(key);

    /// <summary>The text cleaned up for display (see <see cref="Clean"/>), or null.</summary>
    public string? FindDisplay(string? key) => Find(key) is { } text ? Clean(text) : null;

    public static LocalisationIndex Load(GameFileSystem fileSystem, string language = "english")
    {
        var suffix = $"_l_{language}.yml";
        var files = fileSystem.EnumerateFiles("localisation", "*.yml");
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fallbacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Lowest priority first, so later layers overwrite: other languages of the mod, then
        // vanilla, then the mod in the wanted language.
        var ordered = files
            .Where(f => f.Layer == FileLayer.Mod && !f.RelativePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Concat(files.Where(f => f.Layer != FileLayer.Mod && f.RelativePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Layer))
            .Concat(files.Where(f => f.Layer == FileLayer.Mod && f.RelativePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));

        foreach (var file in ordered)
        {
            string text;
            try
            {
                text = Encoding.UTF8.GetString(File.ReadAllBytes(file.FullPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            var isFallback = !file.RelativePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            foreach (var (key, value) in Parse(text))
            {
                texts[key] = value;
                if (isFallback)
                    fallbacks.Add(key);
                else
                    fallbacks.Remove(key);
            }
        }
        return new LocalisationIndex(texts, fallbacks);
    }

    /// <summary>Entries of one .yml file: <c>key:0 "text"</c> lines under the <c>l_language:</c> header.</summary>
    public static IEnumerable<(string Key, string Value)> Parse(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var match = EntryPattern().Match(line);
            if (match.Success)
                yield return (match.Groups["key"].Value, match.Groups["value"].Value.Replace("\\\"", "\""));
        }
    }

    /// <summary>Drops colour codes (<c>§Y…§!</c>) and turns <c>\n</c> escapes into line breaks.</summary>
    public static string Clean(string text) => ColourCode().Replace(text, "").Replace("\\n", "\n");

    // The value runs to the last quote on the line; anything after it is a comment.
    [GeneratedRegex("""^\s*(?<key>[^\s:#"]+):\d*\s*"(?<value>.*)"[^"]*$""")]
    private static partial Regex EntryPattern();

    [GeneratedRegex("§[!A-Za-z0-9]")]
    private static partial Regex ColourCode();
}
