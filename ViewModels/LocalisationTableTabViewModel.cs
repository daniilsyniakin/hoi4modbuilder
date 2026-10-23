using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Game.Localisation;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels;

/// <summary>One language of one key: the text as stored (with <c>\n</c> escapes), edited in place.</summary>
public sealed partial class LocalisationCellViewModel(LocalisationTableTabViewModel owner, string key, string language) : ViewModelBase
{
    private string _committed = "";

    public string Key { get; } = key;

    public string Language { get; } = language;

    /// <summary>The file it is written in; null when the key has no text in this language.</summary>
    public string? File { get; private set; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial bool IsMissing { get; private set; } = true;

    /// <summary>Takes the stored text; what the user is typing stays.</summary>
    public void Load(string? value, string? file)
    {
        if (Text == _committed)
            Text = value ?? "";
        _committed = value ?? "";
        File = file;
        IsMissing = value is null;
    }

    /// <summary>Enter or leaving the cell: writes the text to the language's file.</summary>
    [RelayCommand]
    public void Commit()
    {
        if (Text == _committed)
            return;
        if (Text.Length == 0 && IsMissing)
            return;
        _committed = Text;
        owner.Write(this);
    }

    /// <summary>Esc: back to the stored text.</summary>
    [RelayCommand]
    public void Revert() => Text = _committed;
}

public sealed class LocalisationRowViewModel(string key, IReadOnlyList<LocalisationCellViewModel> cells)
{
    public string Key { get; } = key;

    public IReadOnlyList<LocalisationCellViewModel> Cells { get; } = cells;

    public bool IsUntranslated => Cells.Any(c => c.IsMissing);
}

/// <summary>
/// Every localisation key of the mod in a table, one column per language: what is missing
/// shows at a glance, and texts are typed straight into the cells.
/// </summary>
public sealed partial class LocalisationTableTabViewModel : DocumentTabViewModel
{
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(300);

    private readonly ScriptWorkspace _workspace;
    private readonly LocalisationEditor _editor;
    private readonly Action<string?> _reportError;
    private readonly Action<string, int?> _openFile;
    private readonly DispatcherTimer _reloadTimer;
    private readonly HashSet<string> _addedLanguages = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, LocalisationRowViewModel> _rows = new(StringComparer.Ordinal);
    private List<LocalisationRowViewModel> _all = [];

    public LocalisationTableTabViewModel(ScriptWorkspace workspace, LocalisationEditor editor, Action<string?> reportError,
        Action<string, int?> openFile, Action<DocumentTabViewModel> close)
        : base(Path.Combine(workspace.RootPath, "localisation", "localisation.yml"), workspace.RootPath, close)
    {
        _workspace = workspace;
        _editor = editor;
        _reportError = reportError;
        _openFile = openFile;
        _reloadTimer = new DispatcherTimer { Interval = ReloadDelay };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            Reload();
        };
        workspace.Changed += OnFileChanged;
        Reload();
    }

    public override string Name => "Локалізація";

    public override string Breadcrumbs => "Локалізація мода";

    [ObservableProperty]
    public partial IReadOnlyList<string> Languages { get; private set; } = [];

    /// <summary>Game languages the mod has no column for yet.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<LanguageChoice> MissingLanguages { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<LocalisationRowViewModel> Rows { get; private set; } = [];

    [ObservableProperty]
    public partial string? Search { get; set; }

    [ObservableProperty]
    public partial bool OnlyUntranslated { get; set; }

    [ObservableProperty]
    public partial int KeyCount { get; private set; }

    [ObservableProperty]
    public partial int UntranslatedCount { get; private set; }

    public bool IsEmpty => Rows.Count == 0;

    partial void OnSearchChanged(string? value) => Filter();

    partial void OnOnlyUntranslatedChanged(bool value) => Filter();

    partial void OnRowsChanged(IReadOnlyList<LocalisationRowViewModel> value) => OnPropertyChanged(nameof(IsEmpty));

    /// <summary>A column for a language the mod isn't translated into yet: every key shows as missing there.</summary>
    [RelayCommand]
    private void AddLanguage(LanguageChoice? choice)
    {
        if (choice is null)
            return;
        _addedLanguages.Add(choice.Code);
        Reload();
    }

    /// <summary>The key in its file, in the text editor.</summary>
    [RelayCommand]
    private void OpenKey(LocalisationRowViewModel? row)
    {
        if (row?.Cells.FirstOrDefault(c => c.File is not null) is not { File: { } file } || _workspace.ReadText(file) is not { } text)
            return;
        _openFile(file, LocalisationFile.FindValue(text, row.Key)?.Start);
    }

    internal void Write(LocalisationCellViewModel cell)
    {
        var row = _rows.GetValueOrDefault(cell.Key);
        // A key only in other languages goes into the matching file of this one.
        var source = row?.Cells.FirstOrDefault(c => c != cell && c.File is not null) is { File: { } file } other
            ? (file, other.Language)
            : ((string, string)?)null;
        _reportError(_editor.SetText(cell.Key, LocalisationFile.Unescape(cell.Text), cell.Language, source));
    }

    public void Reload()
    {
        var folder = Path.Combine(_workspace.RootPath, "localisation");
        var files = new List<(string, string)>();
        if (Directory.Exists(folder))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var path in Directory.EnumerateFiles(folder, "*.yml", options))
            {
                if (_workspace.ReadText(path) is { } text)
                    files.Add((path, text));
            }
        }
        var table = LocalisationTable.Read(files);

        var languages = table.Languages.Concat(_addedLanguages.Where(l => !table.Languages.Contains(l, StringComparer.OrdinalIgnoreCase))).ToList();
        if (!languages.SequenceEqual(Languages))
        {
            Languages = languages;
            _rows = new Dictionary<string, LocalisationRowViewModel>(StringComparer.Ordinal); // new columns: new rows
        }
        MissingLanguages = LocalisationService.Languages
            .Where(l => l.Code is not null && !languages.Contains(l.Code, StringComparer.OrdinalIgnoreCase))
            .Select(l => new LanguageChoice(l.Code!, l.Title))
            .ToList();

        var rows = new Dictionary<string, LocalisationRowViewModel>(StringComparer.Ordinal);
        foreach (var key in table.Keys)
        {
            var row = _rows.GetValueOrDefault(key)
                      ?? new LocalisationRowViewModel(key, languages.Select(l => new LocalisationCellViewModel(this, key, l)).ToList());
            foreach (var cell in row.Cells)
                cell.Load(table.Value(key, cell.Language), table.File(key, cell.Language));
            rows[key] = row;
        }
        _rows = rows;
        _all = table.Keys.Select(k => rows[k]).ToList();
        KeyCount = _all.Count;
        UntranslatedCount = _all.Count(r => r.IsUntranslated);
        Filter();
    }

    private void Filter()
    {
        var words = (Search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Rows = _all
            .Where(r => !OnlyUntranslated || r.IsUntranslated)
            .Where(r => words.All(w => r.Key.Contains(w, StringComparison.OrdinalIgnoreCase)
                                       || r.Cells.Any(c => c.Text.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    private void OnFileChanged(string path)
    {
        if (!path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(path))
            return;
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }

    public override void ReloadFromDisk()
    {
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }

    public override void Discard()
    {
        _reloadTimer.Stop();
        _workspace.Changed -= OnFileChanged;
    }
}

/// <summary>A language that can be added as a column: its folder name and how it is called.</summary>
public sealed record LanguageChoice(string Code, string Title);
