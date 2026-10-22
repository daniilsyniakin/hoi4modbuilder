using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using HoI4ModBuilder.Clausewitz.Completion;
using HoI4ModBuilder.Services;
using HoI4ModBuilder.ViewModels;

namespace HoI4ModBuilder.Editor;

/// <summary>
/// Shows the completion popup: automatically while a word is being typed, or on Ctrl+Space.
/// </summary>
public sealed class CompletionController
{
    private const int MaxSyncParseLength = 500_000;

    private readonly TextEditor _editor;
    private CompletionWindow? _window;

    public CompletionController(TextEditor editor)
    {
        _editor = editor;
        editor.TextArea.TextEntered += OnTextEntered;
        editor.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>The tab being edited; completion only runs for script tabs.</summary>
    public EditorTabViewModel? Tab { get; set; }

    public Func<ScriptVocabulary?> Vocabulary { get; set; } = () => null;

    /// <summary>Sprites for thumbnails and for completing icon values.</summary>
    public Func<GameAssets?> Assets { get; set; } = () => null;

    public void Close() => _window?.Close();

    /// <param name="isExplicit">Ctrl+Space or a follow-up after inserting <c>key = </c>: show even with nothing typed.</param>
    /// <param name="onlyKnownValues">Skip the popup unless the vocabulary knows values for the key.</param>
    public void Show(bool isExplicit, bool onlyKnownValues = false)
    {
        if (Tab?.Analyzer is not { } analyzer)
            return;

        // Re-parsing a huge file on every keystroke would freeze typing; popups that open by
        // themselves wait for the background parse there. Ctrl+Space always works.
        var tree = isExplicit || _editor.Document.TextLength < MaxSyncParseLength
            ? analyzer.GetCurrentTree()
            : analyzer.FreshTree;
        if (tree is null)
            return;
        var caret = _editor.CaretOffset;
        var context = ScriptCompletion.GetContext(tree, caret);
        if (context.Position == CompletionPosition.None || (!isExplicit && context.Prefix.Length == 0))
            return;

        var assets = Assets();
        var items = ScriptCompletion.GetItems(context, Vocabulary(), Tab.Folder, tree);
        if (assets is not null && context.Position == CompletionPosition.Value)
            items = AddSprites(items, context, assets);
        if (items.Count == 0 || (onlyKnownValues && !items.Any(i => i.Kind == CompletionItemKind.Value)))
            return;

        _window?.Close();
        var window = new CompletionWindow(_editor.TextArea)
        {
            StartOffset = context.ReplaceStart,
            EndOffset = caret,
            CloseWhenCaretAtBeginning = true,
        };
        foreach (var item in items)
            window.CompletionList.CompletionData.Add(new ScriptCompletionData(item, context, this, assets));
        window.Closed += (_, _) =>
        {
            if (_window == window)
                _window = null;
        };

        _window = window;
        window.Show();
        // As in JetBrains IDEs, something is always selected so Enter/Tab accept right away.
        if (context.Prefix.Length > 0)
            window.CompletionList.SelectItem(context.Prefix);
        else
            window.CompletionList.SelectedItem = window.CompletionList.CompletionData[0];
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_window is not null)
        {
            // Nothing left that matches what has been typed: get out of the way.
            if (_window.CompletionList.CurrentList is { Count: 0 })
                _window.Close();
            return;
        }

        if (e.Text is { Length: 1 } text && IsWordCharacter(text[0]))
            Show(isExplicit: false);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        {
            Show(isExplicit: true);
            e.Handled = true;
        }
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c is '_' or '@' or '.' or ':';

    /// <summary>
    /// The vocabulary only remembers the most common values per key. When a key's values are
    /// sprites (<c>icon = GFX_goal_…</c>), offer every sprite of that family instead, including
    /// ones the mod defines.
    /// </summary>
    private static IReadOnlyList<CompletionItem> AddSprites(
        IReadOnlyList<CompletionItem> items, CompletionContext context, GameAssets assets)
    {
        const string spritePrefix = "GFX_";
        const int maxSprites = 3000;

        var known = items.Where(i => i.Kind == CompletionItemKind.Value).ToList();
        var sprites = known.Where(i => i.Label.StartsWith(spritePrefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (sprites.Count == 0 || sprites.Count * 2 < known.Count)
            return items;

        // "GFX_goal_x", "GFX_goal_y" → family "GFX_goal_".
        var family = sprites
            .Select(i => FamilyOf(i.Label))
            .GroupBy(f => f, StringComparer.OrdinalIgnoreCase)
            .MaxBy(g => g.Count())!.Key;

        var present = items.Select(i => i.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extra = assets.Sprites.All
            .Where(s => s.Name.StartsWith(family, StringComparison.OrdinalIgnoreCase)
                        && s.Name.Contains(context.Prefix, StringComparison.OrdinalIgnoreCase)
                        && !present.Contains(s.Name))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxSprites)
            .Select(s => new CompletionItem(s.Name, CompletionItemKind.Value, "спрайт", 0, false));

        // Known (frequently used) values first, then the rest of the family.
        var result = items.Where(i => i.Kind != CompletionItemKind.Word).Concat(extra).ToList();
        result.AddRange(items.Where(i => i.Kind == CompletionItemKind.Word));
        return result;
    }

    private static string FamilyOf(string sprite)
    {
        var second = sprite.IndexOf('_', 4);
        return second < 0 ? sprite : sprite[..(second + 1)];
    }
}
