using System.Globalization;
using System.Text;

namespace HoI4ModBuilder.Game.Tooltips;

/// <summary>A piece of tooltip text in one of the game's text colours (<c>Y</c>, <c>G</c>, <c>R</c>, <c>H</c>…); null is the default.</summary>
public sealed record TextRun(string Text, char? Colour = null);

/// <summary>One tooltip line, indented by nesting (scopes, <c>if</c> blocks, an idea's modifiers).</summary>
public sealed record TooltipLine(int Indent, IReadOnlyList<TextRun> Runs)
{
    public string PlainText => string.Concat(Runs.Select(r => r.Text));

    public override string ToString() => new string(' ', Indent * 2) + PlainText;
}

/// <summary>
/// The game's localisation markup made into coloured runs: <c>§Y…§!</c> colours, <c>$NAME|fmt$</c>
/// placeholders filled with values formatted as the game does (<c>$VAL|+=%$</c> → <c>+5%</c> in green),
/// <c>£icons£</c> and <c>@$TAG$</c> flags dropped.
/// </summary>
public static class GameText
{
    /// <param name="values">
    /// Placeholder values: a <see cref="decimal"/> (formatted by the placeholder's format), a string,
    /// or runs. Placeholders without a value are left out.
    /// </param>
    public static IReadOnlyList<TextRun> Render(string template, IReadOnlyDictionary<string, object>? values = null)
    {
        var runs = new List<TextRun>();
        var colours = new Stack<char>();
        var text = new StringBuilder();
        char? Current() => colours.Count > 0 ? colours.Peek() : null;

        void Flush()
        {
            if (text.Length > 0)
                Add(runs, text.ToString(), Current());
            text.Clear();
        }

        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            var next = i + 1 < template.Length ? template[i + 1] : '\0';
            switch (c)
            {
                case '§' when next != '\0':
                    Flush();
                    if (next == '!')
                    {
                        if (colours.Count > 0)
                            colours.Pop();
                    }
                    else
                    {
                        colours.Push(next);
                    }
                    i++;
                    break;
                case '\\' when next == 'n':
                    text.Append('\n');
                    i++;
                    break;
                case '£':
                {
                    // £icon£ or £icon followed by a space.
                    var close = template.IndexOf('£', i + 1);
                    var space = template.IndexOfAny([' ', '\n', '\\'], i + 1);
                    i = close >= 0 && (space < 0 || close < space) ? close : (space < 0 ? template.Length : space - 1);
                    break;
                }
                case '@' when next == '$':
                {
                    // A flag before a country name.
                    var close = template.IndexOf('$', i + 2);
                    i = close < 0 ? template.Length : close;
                    break;
                }
                case '$':
                {
                    var close = template.IndexOf('$', i + 1);
                    if (close < 0)
                    {
                        text.Append(c);
                        break;
                    }
                    Flush();
                    var placeholder = template[(i + 1)..close];
                    var bar = placeholder.IndexOf('|');
                    var name = bar < 0 ? placeholder : placeholder[..bar];
                    var format = bar < 0 ? "" : placeholder[(bar + 1)..];
                    if (values?.GetValueOrDefault(name) is { } value)
                        Insert(runs, value, format, Current());
                    i = close;
                    break;
                }
                default:
                    text.Append(c);
                    break;
            }
        }
        Flush();
        return runs;
    }

    /// <summary>A number as the game shows it for a format like <c>+=%</c>, <c>0</c>, <c>.1</c>, <c>-2%</c>, with its colour.</summary>
    public static TextRun Number(decimal value, string format = "")
    {
        var percent = format.Contains('%');
        if (percent)
            value *= 100;
        var digit = format.FirstOrDefault(char.IsDigit);
        var text = digit != '\0'
            ? value.ToString("F" + (digit - '0'), CultureInfo.InvariantCulture)
            : Trim(value);
        var coloured = format.Contains('+') || format.Contains('-');
        if (value > 0 && (coloured || format.Contains('=')))
            text = "+" + text;
        if (percent)
            text += "%";

        char? colour = Letter(format);
        if (coloured && value != 0)
            colour = (value > 0) ^ format.Contains('-') ? 'G' : 'R';
        return new TextRun(text, colour);
    }

    /// <summary>Without the trailing zeros: 0.50 → 0.5, 100.00 → 100.</summary>
    public static string Trim(decimal value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Plain(IEnumerable<TextRun> runs) => string.Concat(runs.Select(r => r.Text));

    private static void Insert(List<TextRun> runs, object value, string format, char? colour)
    {
        var letter = Letter(format) ?? colour;
        switch (value)
        {
            case decimal number:
            {
                var run = Number(number, format);
                Add(runs, run.Text, run.Colour ?? colour);
                break;
            }
            case IEnumerable<TextRun> inner:
                foreach (var run in inner)
                    Add(runs, run.Text, run.Colour ?? letter);
                break;
            default:
            {
                var text = value.ToString() ?? "";
                if (format.Contains('U') && text.Length > 0)
                    text = char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
                Add(runs, text, letter);
                break;
            }
        }
    }

    /// <summary>The colour letter of a format: <c>Y</c> in <c>|Y</c>, <c>H</c> in <c>|0H</c>; <c>U</c> (upper-case) isn't one.</summary>
    private static char? Letter(string format)
    {
        foreach (var c in format)
        {
            if (char.IsLetter(c) && c != 'U')
                return c;
        }
        return null;
    }

    /// <summary>Appends, merging with the previous run of the same colour.</summary>
    private static void Add(List<TextRun> runs, string text, char? colour)
    {
        if (text.Length == 0)
            return;
        if (runs.Count > 0 && runs[^1].Colour == colour)
            runs[^1] = runs[^1] with { Text = runs[^1].Text + text };
        else
            runs.Add(new TextRun(text, colour));
    }
}
