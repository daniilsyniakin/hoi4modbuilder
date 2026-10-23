using System.Text.RegularExpressions;

namespace HoI4ModBuilder.Game.Localisation;

/// <summary>
/// All localisation keys of a mod side by side, one column per language: what is written where,
/// and what is missing.
/// </summary>
public sealed partial class LocalisationTable
{
    private readonly Dictionary<(string Key, string Language), (string Value, string File)> _cells;

    private LocalisationTable(List<string> keys, List<string> languages, Dictionary<(string, string), (string, string)> cells)
    {
        Keys = keys;
        Languages = languages;
        _cells = cells;
    }

    /// <summary>Keys in the order they first appear (files by path, then lines).</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>Languages the mod has files for (<c>english</c>, <c>russian</c>…), English first.</summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>The raw text (escapes kept) of <paramref name="key"/> in <paramref name="language"/>, or null.</summary>
    public string? Value(string key, string language) => _cells.TryGetValue((key, language), out var cell) ? cell.Value : null;

    /// <summary>The file holding <paramref name="key"/> in <paramref name="language"/>, or null.</summary>
    public string? File(string key, string language) => _cells.TryGetValue((key, language), out var cell) ? cell.File : null;

    /// <param name="files">The mod's .yml files with their current text.</param>
    public static LocalisationTable Read(IEnumerable<(string Path, string Text)> files)
    {
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cells = new Dictionary<(string, string), (string, string)>();
        foreach (var (path, text) in files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (LanguageOf(text, path) is not { } language)
                continue;
            languages.Add(language);
            foreach (var (key, value) in LocalisationIndex.Parse(text))
            {
                if (seen.Add(key))
                    keys.Add(key);
                // The game keeps the first definition of a key; so does the table.
                cells.TryAdd((key, language), (StoredForm(value), path));
            }
        }
        var ordered = languages
            .OrderBy(l => l.Equals("english", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new LocalisationTable(keys, ordered, cells);
    }

    /// <summary>The language of a file: its <c>l_english:</c> header, or the <c>_l_english.yml</c> suffix.</summary>
    public static string? LanguageOf(string text, string path)
    {
        var header = Header().Match(text);
        if (header.Success)
            return header.Groups["language"].Value;
        var suffix = Suffix().Match(Path.GetFileName(path));
        return suffix.Success ? suffix.Groups["language"].Value : null;
    }

    /// <summary>
    /// Where a key of <paramref name="language"/> goes when it is only in another language's file:
    /// the same file name and folder with the language swapped (<c>english/ukr_l_english.yml</c> →
    /// <c>russian/ukr_l_russian.yml</c>).
    /// </summary>
    public static string Mirror(string path, string fromLanguage, string toLanguage)
    {
        var directory = Path.GetDirectoryName(path) ?? "";
        if (Path.GetFileName(directory).Equals(fromLanguage, StringComparison.OrdinalIgnoreCase))
            directory = Path.Combine(Path.GetDirectoryName(directory) ?? "", toLanguage);
        var name = Path.GetFileName(path);
        var suffix = LocalisationFile.Suffix(fromLanguage);
        name = name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^suffix.Length] + LocalisationFile.Suffix(toLanguage)
            : Path.GetFileNameWithoutExtension(name) + LocalisationFile.Suffix(toLanguage);
        return Path.Combine(directory, name);
    }

    // LocalisationIndex.Parse unescapes quotes; the table shows the text as stored.
    private static string StoredForm(string value) => value.Replace("\"", "\\\"");

    [GeneratedRegex(@"^﻿?\s*l_(?<language>[a-z_]+):", RegexOptions.Multiline)]
    private static partial Regex Header();

    [GeneratedRegex(@"_l_(?<language>[a-z_]+)\.yml$", RegexOptions.IgnoreCase)]
    private static partial Regex Suffix();
}
