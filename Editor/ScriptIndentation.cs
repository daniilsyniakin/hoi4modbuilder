using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Indentation;

namespace HoI4ModBuilder.Editor;

/// <summary>
/// Brace-aware indentation: a new line after <c>{</c> is indented one tab deeper, and a
/// <c>}</c> typed at the start of a line lines up with its opening brace.
/// </summary>
public sealed class ScriptIndentationStrategy : IIndentationStrategy
{
    private const string Unit = "\t"; // Paradox scripts are tab-indented.

    public void IndentLine(TextDocument document, DocumentLine line)
    {
        var previous = line.PreviousLine;
        if (previous is null)
            return;

        var indent = LeadingWhitespace(document, previous);
        if (EndsWithOpenBrace(document.GetText(previous)))
            indent += Unit;
        if (document.GetText(line).TrimStart().StartsWith('}'))
            indent = MatchingOpenBraceIndent(document, line) ?? indent;

        var current = TextUtilities.GetLeadingWhitespace(document, line);
        document.Replace(current.Offset, current.Length, indent);
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine)
    {
        // Re-indenting whole selections would rewrite hand-formatted vanilla code; leave it alone.
    }

    /// <summary>Call after a character was typed: re-aligns a line that now starts with <c>}</c>.</summary>
    public static void OnTextEntered(TextEditor editor, string text)
    {
        if (text != "}")
            return;
        var document = editor.Document;
        var line = document.GetLineByOffset(editor.CaretOffset);
        var beforeCaret = document.GetText(line.Offset, editor.CaretOffset - line.Offset);
        if (beforeCaret.TrimStart() != "}" || MatchingOpenBraceIndent(document, line) is not { } indent)
            return;

        var current = TextUtilities.GetLeadingWhitespace(document, line);
        if (document.GetText(current) != indent)
            document.Replace(current.Offset, current.Length, indent);
    }

    private static string LeadingWhitespace(TextDocument document, DocumentLine line) =>
        document.GetText(TextUtilities.GetLeadingWhitespace(document, line));

    private static bool EndsWithOpenBrace(string lineText)
    {
        var code = StripComment(lineText).TrimEnd();
        return code.EndsWith('{');
    }

    /// <summary>
    /// Walks back counting braces to find the line with the <c>{</c> that the first <c>}</c> on
    /// <paramref name="line"/> closes. Braces in comments and strings are skipped.
    /// </summary>
    private static string? MatchingOpenBraceIndent(TextDocument document, DocumentLine line)
    {
        var depth = 1;
        for (var current = line.PreviousLine; current is not null; current = current.PreviousLine)
        {
            var code = StripComment(document.GetText(current));
            for (var i = code.Length - 1; i >= 0; i--)
            {
                if (code[i] == '}')
                    depth++;
                else if (code[i] == '{' && --depth == 0)
                    return LeadingWhitespace(document, current);
            }
        }
        return null;
    }

    /// <summary>The line without its <c>#</c> comment and without the contents of strings.</summary>
    private static string StripComment(string line)
    {
        var chars = line.ToCharArray();
        var inString = false;
        for (var i = 0; i < chars.Length; i++)
        {
            switch (chars[i])
            {
                case '"':
                    inString = !inString;
                    break;
                case '#' when !inString:
                    return new string(chars, 0, i);
                default:
                    if (inString)
                        chars[i] = ' ';
                    break;
            }
        }
        return new string(chars);
    }
}
