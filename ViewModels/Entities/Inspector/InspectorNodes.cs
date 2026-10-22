using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Clausewitz.Editing;
using HoI4ModBuilder.Game.Schema;
using HoI4ModBuilder.Game.Validation;

namespace HoI4ModBuilder.ViewModels.Entities.Inspector;

/// <summary>
/// Where a property sits inside an entity: the chain of keys from the entity's block, each with
/// its occurrence among equal keys (<c>prerequisite#1</c> is the second prerequisite). Survives
/// re-parsing, unlike offsets.
/// </summary>
public sealed record InspectorPath(ImmutableArray<(string Key, int Occurrence)> Steps)
{
    public static InspectorPath Root { get; } = new([]);

    public InspectorPath Append(string key, int occurrence) => new(Steps.Add((key, occurrence)));

    public InspectorPath Parent => new(Steps.IsEmpty ? Steps : Steps.RemoveAt(Steps.Length - 1));

    public override string ToString() => string.Join("/", Steps.Select(s => $"{s.Key}#{s.Occurrence}"));

    public bool Equals(InspectorPath? other) => other is not null && ToString() == other.ToString();

    public override int GetHashCode() => ToString().GetHashCode();
}

/// <summary>Applies inspector changes to the script.</summary>
public interface IInspectorEditor
{
    void SetValue(InspectorPath property, string value);

    /// <param name="valueText">Script text of the value: <c>yes</c>, <c>10</c>, <c>{ }</c>…</param>
    void AddProperty(InspectorPath block, string key, string valueText);

    void Remove(InspectorPath property);

    void AddItem(InspectorPath block, string value);

    void RemoveItem(InspectorPath block, int index);
}

/// <summary>What the inspector can offer instead of typing.</summary>
public interface IInspectorSuggestions
{
    /// <summary>Keys that usually appear inside a block of <paramref name="blockKey"/> (null: the entity itself).</summary>
    IReadOnlyList<string> Keys(string? blockKey);

    /// <summary>Values <paramref name="key"/> usually takes there, and entities it can name.</summary>
    IReadOnlyList<string> Values(string? blockKey, string key);

    /// <summary>The script text a freshly added <paramref name="key"/> starts with.</summary>
    string DefaultValue(string? blockKey, string key);

    /// <summary>A human-readable hint for a value, e.g. the localised name of a focus id.</summary>
    string? Describe(string value);

    /// <summary>The game's documented commands; empty until the documentation is read.</summary>
    ScriptSchema Schema { get; }

    /// <summary>What can be added to a script block: documented commands and the mod's scripted ones, most used first.</summary>
    IReadOnlyList<CommandChoice> Commands(ScriptContext context);
}

/// <summary>A command offered by the picker of a script block.</summary>
/// <param name="Template">The value it is inserted with, e.g. <c>{ ideology = neutrality popularity = 0.05 }</c>.</param>
public sealed record CommandChoice(string Name, string Category, string Description, string Template);

/// <summary>
/// A field whose value is chosen on the canvas (e.g. clicking the anchor focus) instead of typed.
/// </summary>
/// <param name="Clear">Replaces the plain "remove" when removing needs more care (keeping a position).</param>
/// <param name="Placeholder">Shown while the property isn't set.</param>
public sealed record FieldPicker(Action Pick, Action? Clear, string Placeholder);

/// <summary>A row of the inspector.</summary>
public abstract partial class InspectorNode : ViewModelBase
{
    protected InspectorNode(string key, InspectorPath path, int depth, bool isMissing, IInspectorEditor editor)
    {
        Key = key;
        Path = path;
        Depth = depth;
        IsMissing = isMissing;
        Editor = editor;
        Label = PropertyLabels.Label(key);
        RemoveCommand = new RelayCommand(Remove, () => !IsMissing);
    }

    public string Key { get; }

    public string Label { get; }

    /// <summary>The label is a translation, so the script key is shown next to it.</summary>
    public bool ShowKey => !string.Equals(Key, Label, StringComparison.Ordinal);

    public InspectorPath Path { get; }

    public int Depth { get; }

    /// <summary>A property the entity could have but doesn't: shown dimmed, filling it in adds it.</summary>
    public bool IsMissing { get; }

    public bool IsPresent => !IsMissing;

    protected IInspectorEditor Editor { get; }

    public IRelayCommand RemoveCommand { get; }

    protected virtual void Remove() => Editor.Remove(Path);

    /// <summary>A problem found here by the mod check; drawn as a coloured bar with the message.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(HasWarning))]
    public partial Problem? Problem { get; set; }

    public bool HasError => Problem?.Severity == ProblemSeverity.Error;

    public bool HasWarning => Problem?.Severity == ProblemSeverity.Warning;

    /// <summary>What the game's documentation says about this command (shown on hover).</summary>
    [ObservableProperty]
    public partial string? Documentation { get; set; }

    /// <summary>The parts every row takes over from a newer build.</summary>
    protected void UpdateCommon(InspectorNode newer)
    {
        Problem = newer.Problem;
        Documentation = newer.Documentation;
    }

    /// <summary>Whether <paramref name="other"/> is the same row in a newer build (so this one can be kept).</summary>
    public bool Matches(InspectorNode other) =>
        other.GetType() == GetType() && other.IsMissing == IsMissing && other.Path.Equals(Path);

    /// <summary>Takes over the values of a newer build of the same row.</summary>
    public abstract void UpdateFrom(InspectorNode other);

    /// <summary>
    /// Makes <paramref name="target"/> match <paramref name="source"/>, keeping rows that still
    /// exist so focus, caret and expansion survive a refresh.
    /// </summary>
    public static void Sync(ObservableCollection<InspectorNode> target, IReadOnlyList<InspectorNode> source)
    {
        var result = new List<InspectorNode>(source.Count);
        var available = target.ToList();
        foreach (var node in source)
        {
            var existing = available.FirstOrDefault(n => n.Matches(node));
            if (existing is null)
            {
                result.Add(node);
                continue;
            }
            available.Remove(existing);
            existing.UpdateFrom(node);
            result.Add(existing);
        }

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!result.Contains(target[i]))
                target.RemoveAt(i);
        }
        for (var i = 0; i < result.Count; i++)
        {
            if (i < target.Count && target[i] == result[i])
                continue;
            var current = target.IndexOf(result[i]);
            if (current >= 0)
                target.Move(current, i);
            else
                target.Insert(i, result[i]);
        }
    }
}

public enum ScalarKind
{
    /// <summary>Free text with suggestions to pick from.</summary>
    Text,
    Number,
    /// <summary><c>yes</c>/<c>no</c>.</summary>
    Bool,
    /// <summary>Shown, not edited here (ids are renamed with F2; colours etc. in code).</summary>
    ReadOnly,
}

/// <summary><c>key = value</c>.</summary>
public sealed partial class ScalarNode : InspectorNode
{
    private static readonly TimeSpan NumberCommitDelay = TimeSpan.FromMilliseconds(700);

    private readonly DispatcherTimer _numberTimer;
    private string _committed;
    private bool _loading;

    public ScalarNode(string key, InspectorPath path, int depth, bool isMissing, IInspectorEditor editor,
        ScalarKind kind, string value, IReadOnlyList<string> suggestions)
        : base(key, path, depth, isMissing, editor)
    {
        Kind = kind;
        Suggestions = suggestions;
        _committed = value;
        _numberTimer = new DispatcherTimer { Interval = NumberCommitDelay };
        _numberTimer.Tick += (_, _) =>
        {
            _numberTimer.Stop();
            Commit();
        };
        Load(value);
    }

    public ScalarKind Kind { get; }

    /// <summary>Set for values picked on the canvas; the field becomes a button that starts the pick.</summary>
    public FieldPicker? Picker { get; init; }

    public bool IsPickable => Picker is not null;

    /// <summary>What the pick button shows: the value, or what to do while it is unset.</summary>
    public string PickText => IsMissing || Text.Length == 0 ? Picker?.Placeholder ?? "" : Text;

    public bool IsText => Kind == ScalarKind.Text && !IsPickable;

    public bool IsNumber => Kind == ScalarKind.Number;

    public bool IsBool => Kind == ScalarKind.Bool;

    public bool IsReadOnly => Kind == ScalarKind.ReadOnly;

    [ObservableProperty]
    public partial IReadOnlyList<string> Suggestions { get; private set; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    [ObservableProperty]
    public partial decimal? Number { get; set; }

    /// <summary>E.g. the localised name of the focus a value points at.</summary>
    [ObservableProperty]
    public partial string? Hint { get; set; }

    public string Placeholder => IsMissing ? "не задано" : "";

    partial void OnIsCheckedChanged(bool value)
    {
        if (_loading)
            return;
        Text = value ? "yes" : "no";
        Commit();
    }

    partial void OnNumberChanged(decimal? value)
    {
        if (_loading)
            return;
        Text = value?.ToString("0.#####", CultureInfo.InvariantCulture) ?? "";
        // Spinner clicks come in bursts; write once they stop.
        _numberTimer.Stop();
        _numberTimer.Start();
    }

    /// <summary>Writes the value: Enter, leaving the field, picking a suggestion, ticking a box.</summary>
    [RelayCommand]
    public void Commit()
    {
        _numberTimer.Stop();
        if (IsReadOnly)
            return;
        var value = Text.Trim();
        if (IsMissing)
        {
            if (value.Length > 0)
                Editor.AddProperty(Path.Parent, Key, ScriptEdits.FormatValue(value));
            return;
        }
        if (value == _committed || value.Length == 0)
            return;
        _committed = value;
        Editor.SetValue(Path, value);
    }

    [RelayCommand]
    private void PickValue() => Picker?.Pick();

    protected override void Remove()
    {
        if (Picker?.Clear is { } clear)
            clear();
        else
            base.Remove();
    }

    partial void OnTextChanged(string value) => OnPropertyChanged(nameof(PickText));

    /// <summary>Esc: back to what the script says.</summary>
    [RelayCommand]
    public void Revert() => Load(_committed);

    public override void UpdateFrom(InspectorNode other)
    {
        var newer = (ScalarNode)other;
        UpdateCommon(newer);
        Suggestions = newer.Suggestions;
        Hint = newer.Hint;
        // Don't clobber what the user is typing; only follow the script when the field is untouched.
        if (Text.Trim() == _committed || IsBool || IsReadOnly)
            Load(newer._committed);
        _committed = newer._committed;
    }

    private void Load(string value)
    {
        _loading = true;
        try
        {
            Text = value;
            IsChecked = value.Equals("yes", StringComparison.OrdinalIgnoreCase);
            Number = decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
        }
        finally
        {
            _loading = false;
        }
    }
}

/// <summary>A bare item of a list block, e.g. one tag of <c>{ GER ENG }</c>.</summary>
public sealed class ListItemViewModel(string text, string? hint, Action remove)
{
    public string Text { get; } = text;

    public string? Hint { get; } = hint;

    public IRelayCommand RemoveCommand { get; } = new RelayCommand(remove);
}

/// <summary><c>key = { … }</c>: an expandable group of nested rows and list items.</summary>
public sealed partial class BlockNode : InspectorNode
{
    private readonly Action<string, bool> _rememberExpansion;
    private readonly string _expansionKey;

    public BlockNode(string key, InspectorPath path, int depth, bool isMissing, IInspectorEditor editor,
        bool isExpanded, string expansionKey, Action<string, bool> rememberExpansion)
        : base(key, path, depth, isMissing, editor)
    {
        _expansionKey = expansionKey;
        _rememberExpansion = rememberExpansion;
        IsExpanded = isExpanded;
    }

    public ObservableCollection<InspectorNode> Children { get; } = [];

    public ObservableCollection<ListItemViewModel> Items { get; } = [];

    /// <summary>Lists (<c>{ A B }</c>) get item chips and an "add item" box.</summary>
    [ObservableProperty]
    public partial bool IsList { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>A one-line preview for the collapsed state.</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> KeySuggestions { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> ItemSuggestions { get; set; } = [];

    /// <summary>The key being picked in the "add property" box.</summary>
    [ObservableProperty]
    public partial string? NewKey { get; set; }

    [ObservableProperty]
    public partial string? NewItem { get; set; }

    /// <summary>Default value text for a key added here; set by the builder.</summary>
    public Func<string, string> DefaultValue { get; init; } = _ => "yes";

    /// <summary>The kind of script inside: effects, triggers, modifiers — or Plain for other blocks.</summary>
    public ScriptContext Context { get; init; }

    /// <summary>Script blocks get a command picker instead of a free key box.</summary>
    public bool IsScript => Context is ScriptContext.Effect or ScriptContext.Trigger or ScriptContext.Modifier;

    public string AddCommandLabel => Context switch
    {
        ScriptContext.Effect => "Ефект",
        ScriptContext.Trigger => "Умова",
        _ => "Модифікатор",
    };

    private CommandPickerViewModel? _picker;

    /// <summary>Builds the picker; set by the builder for script blocks.</summary>
    public Func<CommandPickerViewModel>? PickerFactory { get; set; }

    /// <summary>Created when the picker is first opened: listing hundreds of commands isn't free.</summary>
    public CommandPickerViewModel? Picker => _picker ??= PickerFactory?.Invoke();

    partial void OnIsExpandedChanged(bool value) => _rememberExpansion(_expansionKey, value);

    /// <summary>Turns a missing block into an empty one and opens it.</summary>
    [RelayCommand]
    private void Create()
    {
        _rememberExpansion(_expansionKey, true);
        Editor.AddProperty(Path.Parent, Key, "{ }");
    }

    [RelayCommand]
    public void AddProperty(string? key)
    {
        key = (key ?? NewKey)?.Trim();
        NewKey = null;
        if (string.IsNullOrEmpty(key) || IsMissing || ScriptEdits.FormatValue(key) != key)
            return;
        Editor.AddProperty(Path, key, DefaultValue(key));
    }

    [RelayCommand]
    public void AddItem(string? value)
    {
        value = (value ?? NewItem)?.Trim();
        NewItem = null;
        if (string.IsNullOrEmpty(value) || IsMissing)
            return;
        Editor.AddItem(Path, value);
    }

    public override void UpdateFrom(InspectorNode other)
    {
        var newer = (BlockNode)other;
        UpdateCommon(newer);
        PickerFactory = newer.PickerFactory;
        IsList = newer.IsList;
        Summary = newer.Summary;
        KeySuggestions = newer.KeySuggestions;
        ItemSuggestions = newer.ItemSuggestions;
        Sync(Children, newer.Children);
        Items.Clear();
        foreach (var item in newer.Items)
            Items.Add(item);
    }
}
