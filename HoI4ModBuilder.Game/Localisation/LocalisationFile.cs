using System.Text.RegularExpressions;
using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Editing;

namespace HoI4ModBuilder.Game.Localisation;

/// <summary>Edits inside a localisation .yml file: <c> key:0 "text"</c> lines under <c>l_language:</c>.</summary>
public static partial class LocalisationFile
{
    /// <summary>The header a new file of <paramref name="language"/> starts with.</summary>
    public static string Header(string language) => $"l_{language}:";

    /// <summary>The file name suffix the game requires, e.g. <c>_l_english.yml</c>.</summary>
    public static string Suffix(string language) => $"_l_{language}.yml";

    /// <summary>Where the value of <paramref name="key"/> sits (between the quotes), or null.</summary>
    public static TextSpan? FindValue(string text, string key)
    {
        foreach (Match match in Entry().Matches(text))
        {
            if (match.Groups["key"].Value == key)
                return new TextSpan(match.Groups["value"].Index, match.Groups["value"].Length);
        }
        return null;
    }

    /// <summary>How many keys of the file start with <paramref name="prefix"/>; used to pick a file for a new key.</summary>
    public static int CountKeysWithPrefix(string text, string prefix) =>
        Entry().Matches(text).Count(m => m.Groups["key"].Value.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Replaces the text of an existing key, or null when the file doesn't have it.</summary>
    public static TextEdit? SetValue(string text, string key, string value) =>
        FindValue(text, key) is { } span ? new TextEdit(span, Escape(value)) : null;

    /// <summary>Adds <c> key:0 "value"</c> as the last line of the file.</summary>
    public static TextEdit Append(string text, string key, string value)
    {
        var newLine = ScriptEdits.NewLine(text);
        var end = text.Length;
        var prefix = end > 0 && text[end - 1] != '\n' ? newLine : "";
        return new TextEdit(new TextSpan(end, 0), $"{prefix} {key}:0 \"{Escape(value)}\"{newLine}");
    }

    /// <summary>The whole text of a new file holding one entry.</summary>
    public static string NewFile(string language, string key, string value, string newLine = "\n") =>
        $"{Header(language)}{newLine} {key}:0 \"{Escape(value)}\"{newLine}";

    /// <summary>Line breaks become <c>\n</c> and quotes are escaped, as the game's own files do.</summary>
    public static string Escape(string value) =>
        value.Replace("\"", "\\\"").Replace("\r\n", "\n").Replace("\n", "\\n");

    /// <summary>The raw (still escaped) value turned into what the user edits: real line breaks, plain quotes.</summary>
    public static string Unescape(string raw) => raw.Replace("\\n", "\n").Replace("\\\"", "\"");

    // Same shape the index reads: the value runs to the last quote on the line.
    [GeneratedRegex("""^[ \t]*(?<key>[^\s:#"]+):\d*[ \t]*"(?<value>.*)"[^"\r\n]*\r?$""", RegexOptions.Multiline)]
    private static partial Regex Entry();
}
