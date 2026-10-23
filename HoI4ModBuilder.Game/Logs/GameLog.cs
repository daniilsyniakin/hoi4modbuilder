using System.Text.RegularExpressions;

namespace HoI4ModBuilder.Game.Logs;

/// <summary>One line of the game's <c>error.log</c>.</summary>
/// <param name="Time">Wall-clock time, e.g. <c>21:57:27</c>.</param>
/// <param name="GameDate">In-game date, or <c>no_game_date</c> before a game starts.</param>
/// <param name="Source">The engine source that reported it, e.g. <c>effectimplementation.cpp:123</c>.</param>
public sealed record GameLogEntry(int LineNumber, string Time, string GameDate, string Source, string Message);

public enum LogLinkKind
{
    /// <summary>A file path, with a line when the message gives one.</summary>
    File,
    /// <summary>The id of something the mod defines (a focus, an event…).</summary>
    Entity,
}

/// <summary>A clickable part of a log message.</summary>
/// <param name="Value">The path (relative, with <c>/</c>) or the id.</param>
public sealed record LogLink(int Start, int Length, LogLinkKind Kind, string Value, int? Line);

/// <summary>Reads <c>error.log</c> and finds what its messages talk about.</summary>
public static partial class GameLog
{
    public static IEnumerable<GameLogEntry> Parse(string text)
    {
        var lineNumber = 0;
        GameLogEntry? pending = null;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            lineNumber++;
            var match = EntryLine().Match(raw);
            if (match.Success)
            {
                if (pending is not null)
                    yield return pending;
                pending = new GameLogEntry(lineNumber, match.Groups["time"].Value, match.Groups["date"].Value,
                    match.Groups["source"].Value, match.Groups["message"].Value.Trim());
            }
            else if (pending is not null && raw.Trim().Length > 0)
            {
                // A message that runs on over several lines.
                pending = pending with { Message = pending.Message + "\n" + raw.Trim() };
            }
        }
        if (pending is not null)
            yield return pending;
    }

    /// <summary>
    /// Files and mod ids mentioned in <paramref name="message"/>, in order and not overlapping.
    /// </summary>
    /// <param name="isModId">Whether a word is the id of something the mod defines.</param>
    public static IReadOnlyList<LogLink> FindLinks(string message, Func<string, bool> isModId)
    {
        var links = new List<LogLink>();
        foreach (Match match in FilePath().Matches(message))
        {
            var path = match.Groups["path"];
            // "… in file: x.txt near line: 12" — the line follows the path closely.
            var after = message[(path.Index + path.Length)..];
            var lineMatch = LineAfter().Match(after);
            int? line = lineMatch.Success && lineMatch.Index < 24 ? int.Parse(lineMatch.Groups["line"].Value) : null;
            links.Add(new LogLink(path.Index, path.Length, LogLinkKind.File, path.Value.Replace('\\', '/'), line));
        }

        foreach (Match match in Word().Matches(message))
        {
            if (links.Any(l => match.Index < l.Start + l.Length && l.Start < match.Index + match.Length))
                continue;
            var word = match.Value.TrimEnd('.', ':');
            if (isModId(word))
                links.Add(new LogLink(match.Index, word.Length, LogLinkKind.Entity, word, null));
        }
        return links.OrderBy(l => l.Start).ToList();
    }

    /// <summary>
    /// The message with its specifics (names, paths, numbers) blanked, so messages that say the same
    /// thing about different things group together: <c>Duplicate of «…» added to entity system</c>.
    /// </summary>
    public static string Pattern(string message)
    {
        var pattern = Quoted().Replace(message, "«…»");
        pattern = FilePath().Replace(pattern, "«…»");
        pattern = Specific().Replace(pattern, "«…»");
        pattern = Number().Replace(pattern, "#");
        return pattern;
    }

    // [21:57:27][no_game_date][dlc.cpp:142]: message
    [GeneratedRegex(@"^\[(?<time>[^\]]*)\]\[(?<date>[^\]]*)\]\[(?<source>[^\]]*)\]:\s?(?<message>.*)$")]
    private static partial Regex EntryLine();

    [GeneratedRegex("""(?<![\w/.])(?<path>(?:[\w\-.]+[/\\])+[\w\-. ]*?\.(?:txt|gfx|gui|yml|asset|mod|dds|tga|png|csv|lua|shader|fxh|wav|ogg|mesh|anim|json))(?![\w])""", RegexOptions.IgnoreCase)]
    private static partial Regex FilePath();

    [GeneratedRegex(@"line:?\s*(?<line>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LineAfter();

    [GeneratedRegex(@"[A-Za-z_][\w.:]*")]
    private static partial Regex Word();

    [GeneratedRegex("""'[^']*'|"[^"]*"|«[^»]*»""")]
    private static partial Regex Quoted();

    // Ids: words with an underscore, a dot inside (event ids) or a TAG_ prefix.
    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9]*(?:[_.][A-Za-z0-9]+)+\b")]
    private static partial Regex Specific();

    [GeneratedRegex(@"\d+(?:\.\d+)?")]
    private static partial Regex Number();
}
