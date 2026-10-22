using System.Text;
using Avalonia;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoI4ModBuilder.Editor;
using HoI4ModBuilder.Models;
using HoI4ModBuilder.Services;

namespace HoI4ModBuilder.ViewModels;

public enum SaveState
{
    Saved,
    Pending,
    Failed,
}

/// <summary>
/// An open file. The text is kept in sync with the disk in both directions: edits are written
/// shortly after typing stops, and changes made by other programs are pulled in automatically.
/// </summary>
public sealed partial class EditorTabViewModel : DocumentTabViewModel
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ReloadRetryDelay = TimeSpan.FromMilliseconds(150);
    private const int MaxReloadAttempts = 5;

    private readonly DispatcherTimer _saveTimer;
    private Encoding _encoding;
    private bool _applyingExternalChange;
    private bool _disposed;

    private EditorTabViewModel(string filePath, string rootPath, TextFileContent content, Action<DocumentTabViewModel> close)
        : base(filePath, rootPath, close)
    {
        _encoding = content.Encoding;
        LineEnding = DetectLineEnding(content.Text);

        Document = new TextDocument(content.Text);
        Document.TextChanged += OnDocumentTextChanged;

        if (FileKinds.IsScript(filePath))
        {
            Analyzer = new ScriptAnalyzer(Document);
            Analyzer.Analyzed += (_, _) =>
            {
                OnPropertyChanged(nameof(ProblemsText));
                OnPropertyChanged(nameof(HasErrors));
            };
        }

        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => Flush();
    }

    /// <exception cref="IOException">The file can't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file can't be read.</exception>
    /// <exception cref="InvalidDataException">The file is binary.</exception>
    public static EditorTabViewModel Open(string filePath, string rootPath, Action<DocumentTabViewModel> close) =>
        new(filePath, rootPath, TextFileCodec.Decode(File.ReadAllBytes(filePath)), close);

    public TextDocument Document { get; }

    /// <summary>Background parser for script files; null for other text (localisation, csv…).</summary>
    public ScriptAnalyzer? Analyzer { get; }

    public bool IsScript => Analyzer is not null;

    public bool HasErrors => Analyzer?.ErrorCount > 0;

    /// <summary>"3 помилки" / "Без помилок", for the status bar.</summary>
    public string ProblemsText => Analyzer?.ErrorCount switch
    {
        null => "",
        0 => "Без помилок",
        var n => Ukrainian.Count(n.Value, "помилка", "помилки", "помилок"),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatus), nameof(HasSaveError))]
    public partial SaveState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatus))]
    public partial string? SaveError { get; set; }

    public override bool HasSaveError => State == SaveState.Failed;

    public string SaveStatus => State switch
    {
        SaveState.Pending => "Збереження…",
        SaveState.Failed => SaveError ?? "Помилка збереження",
        _ => "Усі зміни збережено",
    };

    public string EncodingName => TextFileCodec.DisplayName(_encoding);

    [ObservableProperty]
    public partial string LineEnding { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaretPosition))]
    public partial int CaretLine { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaretPosition))]
    public partial int CaretColumn { get; set; } = 1;

    public string CaretPosition => $"{CaretLine}:{CaretColumn}";

    // Editor view state, restored when the tab becomes active again.
    public int SavedCaretOffset { get; set; }
    public Vector SavedScrollOffset { get; set; }

    /// <summary>Writes pending edits to disk immediately.</summary>
    public override void Flush()
    {
        _saveTimer.Stop();
        if (State == SaveState.Saved)
            return;

        try
        {
            var (bytes, encoding) = TextFileCodec.Encode(Document.Text, _encoding);
            File.WriteAllBytes(FilePath, bytes);
            if (!Equals(encoding, _encoding))
            {
                _encoding = encoding;
                OnPropertyChanged(nameof(EncodingName));
            }
            SaveError = null;
            State = SaveState.Saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Stays dirty: the next edit, Ctrl+S or closing the tab retries.
            SaveError = $"Не вдалося зберегти {Name}: {ex.Message}";
            State = SaveState.Failed;
        }
    }

    public override void ReloadFromDisk() => ReloadFromDisk(0);

    private void ReloadFromDisk(int attempt)
    {
        // Unsaved local edits win; they will overwrite the file on the next flush.
        if (_disposed || State != SaveState.Saved)
            return;

        TextFileContent content;
        try
        {
            content = TextFileCodec.Decode(File.ReadAllBytes(FilePath));
        }
        catch (IOException) when (attempt < MaxReloadAttempts)
        {
            // The writer may still hold the file open.
            DispatcherTimer.RunOnce(() => ReloadFromDisk(attempt + 1), ReloadRetryDelay);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return;
        }

        if (!Equals(content.Encoding, _encoding))
        {
            _encoding = content.Encoding;
            OnPropertyChanged(nameof(EncodingName));
        }
        if (content.Text != Document.Text)
            ApplyExternalText(content.Text);
    }

    public override void Dispose()
    {
        if (!_disposed)
            base.Dispose();
    }

    public override void Discard()
    {
        _disposed = true;
        _saveTimer.Stop();
        Document.TextChanged -= OnDocumentTextChanged;
        Analyzer?.Dispose();
    }

    private void OnDocumentTextChanged(object? sender, EventArgs e)
    {
        if (_applyingExternalChange)
            return;
        State = SaveState.Pending;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Replaces only the part that differs, so the caret, scroll position and undo history survive.
    /// </summary>
    private void ApplyExternalText(string text)
    {
        var old = Document.Text;
        var max = Math.Min(old.Length, text.Length);
        var prefix = 0;
        while (prefix < max && old[prefix] == text[prefix])
            prefix++;
        var suffix = 0;
        while (suffix < max - prefix && old[old.Length - 1 - suffix] == text[text.Length - 1 - suffix])
            suffix++;

        _applyingExternalChange = true;
        try
        {
            Document.Replace(prefix, old.Length - prefix - suffix, text.Substring(prefix, text.Length - prefix - suffix));
        }
        finally
        {
            _applyingExternalChange = false;
        }
        LineEnding = DetectLineEnding(text);
    }

    private static string DetectLineEnding(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "CRLF"
        : text.Contains('\n') ? "LF"
        : Environment.NewLine == "\r\n" ? "CRLF" : "LF";
}
