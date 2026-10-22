using HoI4ModBuilder.Clausewitz;
using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.ViewModels;

namespace HoI4ModBuilder.Services;

/// <summary>
/// The one place graphical editors read and change mod files. When a file is open in a text tab,
/// its document is the source of truth (unsaved typing included) and edits go into it, so they
/// show up there and are autosaved; otherwise the file on disk is changed. Every change is
/// recorded in <see cref="History"/> for undo and redo.
/// </summary>
public sealed class ScriptWorkspace(string rootPath, Func<string, EditorTabViewModel?> findOpenTab)
{
    public string RootPath { get; } = rootPath;

    public EditHistory History { get; } = new();

    /// <summary>
    /// Raised on the UI thread when a file's text may have changed: by an edit made here, by
    /// typing in its tab or on disk. Carries the normalised path.
    /// </summary>
    public event Action<string>? Changed;

    /// <summary>Path relative to the mod root with forward slashes, e.g. <c>events/ukraine.txt</c>.</summary>
    public string Relative(string path) => Path.GetRelativePath(RootPath, path).Replace('\\', '/');

    /// <summary>The current text, or null if the file can't be read.</summary>
    public string? ReadText(string path)
    {
        if (findOpenTab(path) is { } tab)
            return tab.Document.Text;
        try
        {
            return TextFileCodec.Decode(File.ReadAllBytes(path)).Text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Groups the edits made until disposal into one undo step called <paramref name="description"/>
    /// ("Переміщення фокуса"). Nested groups join the outermost one.
    /// </summary>
    public IDisposable Action(string description) => History.Begin(description);

    /// <summary>Builds edits against the current text and applies them.</summary>
    /// <returns>An error message, or null on success (including when there was nothing to change).</returns>
    public string? Edit(string path, Func<string, SyntaxTree, IEnumerable<TextEdit>> build)
    {
        try
        {
            var before = ReadForEdit(path);
            var edits = build(before, SyntaxTree.Parse(before)).ToList();
            if (edits.Count == 0)
                return null;
            var after = ScriptEdits.Apply(before, edits);
            Write(path, before, after);
            History.Record(FileChange.Between(path, before, after, created: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"Не вдалося змінити {Path.GetFileName(path)}: {ex.Message}";
        }

        NotifyChanged(path);
        return null;
    }

    /// <summary>Creates a new file (e.g. a localisation file), recorded so that undo removes it again.</summary>
    /// <param name="withByteOrderMark">The game requires it for localisation.</param>
    public string? CreateFile(string path, string text, bool withByteOrderMark)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteNew(path, text, withByteOrderMark);
            History.Record(FileChange.Between(path, "", text, created: true) with { ByteOrderMark = withByteOrderMark });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Не вдалося створити {Path.GetFileName(path)}: {ex.Message}";
        }
        NotifyChanged(path);
        return null;
    }

    /// <summary>Undoes the last action, if the files are still as it left them.</summary>
    /// <returns>An error message, or null.</returns>
    public string? Undo() => Replay(History.PeekUndo(), undo: true);

    public string? Redo() => Replay(History.PeekRedo(), undo: false);

    public void NotifyChanged(string path) => Changed?.Invoke(Paths.Normalize(path));

    private string? Replay(EditHistory.Entry? entry, bool undo)
    {
        if (entry is null)
            return null;

        // All files must be exactly as the action (or its undo) left them, or nothing is touched.
        var current = new Dictionary<string, string?>(Paths.Comparer);
        foreach (var path in entry.Changes.Select(c => c.Path).Distinct(Paths.Comparer))
        {
            var text = File.Exists(path) || findOpenTab(path) is not null ? ReadText(path) : null;
            current[path] = text;
            // Undo starts from the file's last change in the action, redo from its first.
            var change = undo
                ? entry.Changes.Last(c => Paths.AreEqual(c.Path, path))
                : entry.Changes.First(c => Paths.AreEqual(c.Path, path));
            if (!change.Matches(text, change.Fingerprint(after: undo), undo))
            {
                History.Discard(entry, undo);
                return $"Не вдалося {(undo ? "скасувати" : "повторити")} «{entry.Description}»: файл {Path.GetFileName(path)} змінено після цієї дії.";
            }
        }

        try
        {
            // Later changes of the same file sit on top of earlier ones: unwind them in reverse.
            var ordered = undo ? entry.Changes.AsEnumerable().Reverse() : entry.Changes;
            foreach (var change in ordered)
            {
                var text = current[change.Path];
                if (undo && change.Created)
                {
                    Delete(change.Path);
                    current[change.Path] = null;
                }
                else if (!undo && change.Created)
                {
                    WriteNew(change.Path, change.NewText, change.ByteOrderMark);
                    current[change.Path] = change.NewText;
                }
                else
                {
                    var (remove, insert) = undo ? (change.NewText, change.OldText) : (change.OldText, change.NewText);
                    var replaced = text![..change.Start] + insert + text[(change.Start + remove.Length)..];
                    Write(change.Path, text, replaced);
                    current[change.Path] = replaced;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"Не вдалося {(undo ? "скасувати" : "повторити")} «{entry.Description}»: {ex.Message}";
        }

        History.Move(entry, undo);
        foreach (var path in entry.Changes.Select(c => c.Path).Distinct(Paths.Comparer))
            NotifyChanged(path);
        return null;
    }

    private string ReadForEdit(string path) =>
        findOpenTab(path) is { } tab ? tab.Document.Text : TextFileCodec.Decode(File.ReadAllBytes(path)).Text;

    /// <summary>Puts <paramref name="after"/> in place of <paramref name="before"/>, touching only what differs.</summary>
    private void Write(string path, string before, string after)
    {
        if (findOpenTab(path) is { } tab)
        {
            var (start, removed, inserted) = Difference(before, after);
            // One undo step in the text editor too.
            tab.Document.BeginUpdate();
            try
            {
                tab.Document.Replace(start, removed, inserted);
            }
            finally
            {
                tab.Document.EndUpdate();
            }
            tab.Flush();
            return;
        }

        var encoding = TextFileCodec.Decode(File.ReadAllBytes(path)).Encoding;
        var (bytes, _) = TextFileCodec.Encode(after, encoding);
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteNew(string path, string text, bool withByteOrderMark)
    {
        var encoding = new System.Text.UTF8Encoding(withByteOrderMark);
        var (bytes, _) = TextFileCodec.Encode(text, encoding);
        File.WriteAllBytes(path, bytes);
    }

    private void Delete(string path)
    {
        if (File.Exists(path))
            ModFileOperations.Delete(path);
    }

    /// <summary>The single changed region between two texts.</summary>
    internal static (int Start, int Removed, string Inserted) Difference(string before, string after)
    {
        var max = Math.Min(before.Length, after.Length);
        var prefix = 0;
        while (prefix < max && before[prefix] == after[prefix])
            prefix++;
        var suffix = 0;
        while (suffix < max - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
            suffix++;
        return (prefix, before.Length - prefix - suffix, after.Substring(prefix, after.Length - prefix - suffix));
    }
}

/// <summary>One file's part of an action: the changed region, and fingerprints of the whole text around it.</summary>
public sealed record FileChange(string Path, int Start, string OldText, string NewText, bool Created,
    int HashBefore, int LengthBefore, int HashAfter, int LengthAfter)
{
    public bool ByteOrderMark { get; init; }

    public static FileChange Between(string path, string before, string after, bool created)
    {
        var (start, removed, inserted) = ScriptWorkspace.Difference(before, after);
        return new FileChange(Paths.Normalize(path), start, before.Substring(start, removed), inserted, created,
            before.GetHashCode(), before.Length, after.GetHashCode(), after.Length);
    }

    public (int Hash, int Length) Fingerprint(bool after) => after ? (HashAfter, LengthAfter) : (HashBefore, LengthBefore);

    /// <summary>Whether the file's current text is the one the replay expects.</summary>
    public bool Matches(string? text, (int Hash, int Length) expected, bool undo)
    {
        // Redoing a creation expects the file to be gone again.
        if (Created && !undo)
            return text is null;
        return text is not null && text.Length == expected.Length && text.GetHashCode() == expected.Hash;
    }
}

/// <summary>Undo and redo stacks of workspace actions.</summary>
public sealed class EditHistory
{
    private const int Capacity = 200;

    private readonly LinkedList<Entry> _undo = new(); // newest last
    private readonly Stack<Entry> _redo = new();
    private Entry? _open;
    private int _depth;

    public sealed class Entry(string description)
    {
        public string Description { get; } = description;
        public List<FileChange> Changes { get; } = [];
    }

    /// <summary>Raised whenever what can be undone or redone changes.</summary>
    public event EventHandler? Changed;

    public string? UndoDescription => _undo.Last?.Value.Description;

    public string? RedoDescription => _redo.TryPeek(out var entry) ? entry.Description : null;

    internal IDisposable Begin(string description)
    {
        if (_depth++ == 0)
            _open = new Entry(description);
        return new Scope(this);
    }

    internal void Record(FileChange change)
    {
        if (_open is not null)
        {
            _open.Changes.Add(change);
            return;
        }
        var single = new Entry("Зміна");
        single.Changes.Add(change);
        Push(single);
    }

    internal Entry? PeekUndo() => _undo.Last?.Value;

    internal Entry? PeekRedo() => _redo.TryPeek(out var entry) ? entry : null;

    /// <summary>A replayed entry moves to the other stack.</summary>
    internal void Move(Entry entry, bool undone)
    {
        if (undone)
        {
            _undo.RemoveLast();
            _redo.Push(entry);
        }
        else
        {
            _redo.Pop();
            _undo.AddLast(entry);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>An entry that can't be replayed any more (its files changed) is dropped, with everything before it.</summary>
    internal void Discard(Entry entry, bool fromUndo)
    {
        if (fromUndo)
            _undo.Clear();
        else
            _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Push(Entry entry)
    {
        if (entry.Changes.Count == 0)
            return;
        _undo.AddLast(entry);
        if (_undo.Count > Capacity)
            _undo.RemoveFirst();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void End()
    {
        if (--_depth > 0 || _open is null)
            return;
        var entry = _open;
        _open = null;
        Push(entry);
    }

    private sealed class Scope(EditHistory history) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            history.End();
        }
    }
}
