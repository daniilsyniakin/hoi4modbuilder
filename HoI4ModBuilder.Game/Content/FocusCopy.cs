using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Clausewitz.Syntax;

namespace HoI4ModBuilder.Game.Content;

/// <summary>A focus as copied: its whole definition and where it stood on the tree.</summary>
/// <param name="Text">The definition as written, from <c>focus = {</c> to its closing brace.</param>
/// <param name="Indent">The indentation of its first line, for re-indenting the rest.</param>
public sealed record CopiedFocus(string Id, string Text, string Indent, int GridX, int GridY, string? RelativeTo);

/// <summary>
/// Pasting copied focuses as new ones: fresh ids, links among the copies moved to the new ids,
/// links to focuses outside the copy kept, positions shifted to where they are pasted.
/// </summary>
public static class FocusCopy
{
    /// <summary>What all ids start with, up to and including the first <c>_</c> (<c>UKR_</c>); null when they differ.</summary>
    public static string? CommonPrefix(IEnumerable<string> ids)
    {
        string? prefix = null;
        foreach (var id in ids)
        {
            var cut = id.IndexOf('_');
            if (cut <= 0)
                return null;
            var own = id[..(cut + 1)];
            if (prefix is null)
                prefix = own;
            else if (prefix != own)
                return null;
        }
        return prefix;
    }

    /// <summary>
    /// New ids: <paramref name="oldPrefix"/> replaced by <paramref name="newPrefix"/> when given, then
    /// <c>_2</c>, <c>_3</c>… until no id is <paramref name="taken"/> (nor another new one).
    /// </summary>
    public static Dictionary<string, string> NewIds(IEnumerable<string> ids, string? oldPrefix, string? newPrefix, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var baseId = oldPrefix is not null && newPrefix is not null && id.StartsWith(oldPrefix, StringComparison.Ordinal)
                ? newPrefix + id[oldPrefix.Length..]
                : id;
            var candidate = baseId;
            for (var n = 2; used.Contains(candidate); n++)
                candidate = $"{baseId}_{n}";
            used.Add(candidate);
            result[id] = candidate;
        }
        return result;
    }

    /// <summary>
    /// The text of a pasted focus: its ids renamed, and — unless it is placed relative to another
    /// copy — absolute x/y putting it at the same offset from <paramref name="target"/> as it had
    /// from <paramref name="origin"/>. Mutual exclusions with focuses outside the copy are dropped:
    /// the game wants them on both sides, and the originals don't name the copy.
    /// </summary>
    /// <param name="indent">The indentation it goes in at.</param>
    public static string Rewrite(CopiedFocus focus, IReadOnlyDictionary<string, string> renames, (int X, int Y) origin, (int X, int Y) target,
        string indent)
    {
        // A shared focus pasted into a tree becomes one of its focuses.
        var text = focus.Text.StartsWith("shared_focus", StringComparison.Ordinal) ? "focus" + focus.Text["shared_focus".Length..] : focus.Text;

        var tree = SyntaxTree.Parse(text);
        if (tree.Root.Properties().FirstOrDefault()?.Value is BlockSyntax block
            && (focus.RelativeTo is null || !renames.ContainsKey(focus.RelativeTo)))
        {
            var edits = ScriptRead.All(block, "relative_position_id").Select(p => ScriptEdits.RemoveStatement(text, p)).ToList();
            edits.Add(ScriptEdits.SetProperty(text, block, "x", (target.X + focus.GridX - origin.X).ToString()));
            edits.Add(ScriptEdits.SetProperty(text, block, "y", (target.Y + focus.GridY - origin.Y).ToString()));
            text = ScriptEdits.Apply(text, edits);
        }

        tree = SyntaxTree.Parse(text);
        if (tree.Root.Properties().FirstOrDefault()?.Value is BlockSyntax pasted)
        {
            var edits = new List<TextEdit>();
            foreach (var exclusive in ScriptRead.All(pasted, "mutually_exclusive"))
            {
                if (exclusive.Value is not BlockSyntax links)
                    continue;
                var outside = ScriptRead.All(links, "focus").Where(f => f.Value is ScalarSyntax s && !renames.ContainsKey(s.ValueText)).ToList();
                if (outside.Count == links.Statements.Length)
                    edits.Add(ScriptEdits.RemoveStatement(text, exclusive));
                else
                    edits.AddRange(outside.Select(f => ScriptEdits.RemoveStatement(text, f)));
            }
            text = ScriptEdits.Apply(text, edits);
        }

        tree = SyntaxTree.Parse(text);
        text = ScriptEdits.Apply(text, renames.SelectMany(r => Renaming.RenameReferences(tree, r.Key, r.Value)));
        return Reindent(text, focus.Indent, indent);
    }

    /// <summary>Lines after the first move from one indentation to another.</summary>
    private static string Reindent(string text, string from, string to)
    {
        if (from == to)
            return text;
        var lines = text.Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(from, StringComparison.Ordinal))
                lines[i] = to + lines[i][from.Length..];
        }
        return string.Join('\n', lines);
    }
}
