using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Clausewitz.Editing;

/// <summary>Replace <see cref="Span"/> of the original text with <see cref="NewText"/>.</summary>
public readonly record struct TextEdit(TextSpan Span, string NewText);

/// <summary>
/// Builds minimal text edits for structural changes (set a value, add or remove a property), so
/// that everything else in the file — formatting, comments, ordering — stays exactly as written.
/// </summary>
public static class ScriptEdits
{
    /// <summary>A bare word when the value allows it, otherwise a quoted string.</summary>
    public static string FormatValue(string value) =>
        value.Length > 0 && value.All(IsWordCharacter) ? value : Quote(value);

    public static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>Replaces the value of <paramref name="property"/>, keeping quotes if it had them.</summary>
    public static TextEdit ReplaceValue(PropertySyntax property, string value)
    {
        var text = property.Value is ScalarSyntax { IsQuoted: true } ? Quote(value) : FormatValue(value);
        return new TextEdit(property.Value.Span, text);
    }

    /// <summary>
    /// Sets <c>key = value</c> in <paramref name="container"/>: replaces the value of the last
    /// existing <paramref name="key"/> (the one the game uses) or adds the property.
    /// </summary>
    public static TextEdit SetProperty(string text, IStatementContainer container, string key, string value)
    {
        var existing = container.Properties()
            .LastOrDefault(p => p.Key.ValueText.Equals(key, StringComparison.OrdinalIgnoreCase));
        return existing is { Value: ScalarSyntax }
            ? ReplaceValue(existing, value)
            : InsertStatement(text, container, $"{key} = {FormatValue(value)}");
    }

    /// <summary>
    /// Adds <paramref name="statement"/> after the last statement of <paramref name="container"/>,
    /// on its own line with the same indentation; inline in one-line blocks such as <c>{ id = X }</c>.
    /// </summary>
    public static TextEdit InsertStatement(string text, IStatementContainer container, string statement)
    {
        var newLine = NewLine(text);
        if (container is not BlockSyntax block)
        {
            var end = ((ScriptFileSyntax)container).EndOfFile.Position;
            var trimmed = text.AsSpan(0, end).TrimEnd();
            var prefix = trimmed.Length == 0 ? "" : newLine;
            return new TextEdit(new TextSpan(trimmed.Length, end - trimmed.Length), prefix + statement + newLine);
        }

        var open = block.OpenBrace.Span.End;
        var close = block.CloseBrace.IsMissing ? block.Span.End : block.CloseBrace.Position;
        if (!text.AsSpan(open, close - open).ContainsAny('\r', '\n'))
        {
            // One-line block: "{ id = X }" → "{ id = X key = v }", "{}" → "{ key = v }".
            var before = close > 0 && char.IsWhiteSpace(text[close - 1]) ? "" : " ";
            return new TextEdit(new TextSpan(close, 0), before + statement + " ");
        }

        if (block.Statements.LastOrDefault() is { } last)
        {
            var anchor = EndIncludingTrailingComment(text, last.Span.End);
            return new TextEdit(new TextSpan(anchor, 0), newLine + IndentOf(text, last.Span.Start) + statement);
        }

        // Empty multi-line block: indent one level deeper than the closing brace.
        var indent = IndentOf(text, close) + "\t";
        return new TextEdit(new TextSpan(open, 0), newLine + indent + statement);
    }

    /// <summary>
    /// Like <see cref="InsertStatement"/>, for a statement spanning several lines (written with
    /// <c>\n</c> and tabs relative to its first line): every line gets the indentation of the
    /// place it goes to, and the file's line breaks.
    /// </summary>
    public static TextEdit InsertMultiLine(string text, IStatementContainer container, string statement)
    {
        var newLine = NewLine(text);
        var indent = ChildIndent(text, container);
        var lines = statement.Replace("\r\n", "\n").Split('\n');
        var indented = string.Join(newLine, lines.Select((line, i) => i == 0 || line.Length == 0 ? line : indent + line));
        return InsertStatement(text, container, indented);
    }

    /// <summary>The indentation <see cref="InsertStatement"/> gives a new statement in <paramref name="container"/>.</summary>
    public static string ChildIndent(string text, IStatementContainer container)
    {
        if (container is not BlockSyntax block)
            return "";
        if (block.Statements.LastOrDefault() is { } last)
            return IndentOf(text, last.Span.Start);
        var close = block.CloseBrace.IsMissing ? block.Span.End : block.CloseBrace.Position;
        return IndentOf(text, close) + "\t";
    }

    /// <summary>
    /// Removes a statement. When it is alone on its line, the whole line goes (with its comment);
    /// otherwise only the statement and the whitespace after it.
    /// </summary>
    public static TextEdit RemoveStatement(string text, StatementSyntax statement)
    {
        var start = statement.Span.Start;
        var end = statement.Span.End;

        var lineStart = start;
        while (lineStart > 0 && text[lineStart - 1] is ' ' or '\t')
            lineStart--;
        var after = end;
        while (after < text.Length && text[after] is ' ' or '\t')
            after++;

        var startsLine = lineStart == 0 || text[lineStart - 1] == '\n';
        var lineEnd = EndIncludingTrailingComment(text, end);
        var endsLine = lineEnd == text.Length || text[lineEnd] is '\r' or '\n';
        if (startsLine && endsLine)
        {
            if (lineEnd < text.Length && text[lineEnd] == '\r')
                lineEnd++;
            if (lineEnd < text.Length && text[lineEnd] == '\n')
                lineEnd++;
            return new TextEdit(TextSpan.FromBounds(lineStart, lineEnd), "");
        }
        return new TextEdit(TextSpan.FromBounds(start, after), "");
    }

    /// <summary>Applies non-overlapping edits that all refer to positions in the original text.</summary>
    public static string Apply(string text, IEnumerable<TextEdit> edits)
    {
        var result = text;
        foreach (var edit in edits.OrderByDescending(e => e.Span.Start).ThenByDescending(e => e.Span.End))
            result = result.Remove(edit.Span.Start, edit.Span.Length).Insert(edit.Span.Start, edit.NewText);
        return result;
    }

    /// <summary>The whitespace at the start of the line containing <paramref name="position"/>.</summary>
    public static string IndentOf(string text, int position)
    {
        var lineStart = position;
        while (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r'))
            lineStart--;
        var end = lineStart;
        while (end < text.Length && text[end] is ' ' or '\t')
            end++;
        return text[lineStart..end];
    }

    public static string NewLine(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>End of the line's content after <paramref name="position"/>: spaces and a trailing comment.</summary>
    private static int EndIncludingTrailingComment(string text, int position)
    {
        var end = position;
        while (end < text.Length && text[end] is ' ' or '\t')
            end++;
        if (end < text.Length && text[end] == '#')
        {
            while (end < text.Length && text[end] is not ('\r' or '\n'))
                end++;
            return end;
        }
        // Only the spaces are swallowed when the line goes on with something else.
        return end < text.Length && text[end] is not ('\r' or '\n') ? position : end;
    }

    // Mirrors the lexer: anything else needs quotes.
    private static bool IsWordCharacter(char c) =>
        !char.IsWhiteSpace(c) && c is not ('{' or '}' or '=' or '"' or '#' or '<' or '>' or '!' or '?');
}
