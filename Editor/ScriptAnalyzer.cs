using System.Diagnostics;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using HoI4ModBuilder.Clausewitz;

namespace HoI4ModBuilder.Editor;

/// <summary>A diagnostic anchored in the document; its offsets follow edits automatically.</summary>
public sealed class DiagnosticMarker : TextSegment
{
    public DiagnosticMarker(int startOffset, int length, Diagnostic diagnostic)
    {
        StartOffset = startOffset;
        Length = length;
        Diagnostic = diagnostic;
    }

    public Diagnostic Diagnostic { get; }
}

/// <summary>
/// Re-parses a document in the background shortly after typing stops and keeps the latest
/// syntax tree and diagnostics. Diagnostics found on an older snapshot are mapped onto the
/// current text, so squiggles stay in place while the user keeps typing.
/// </summary>
public sealed class ScriptAnalyzer : IDisposable
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    private readonly TextDocument _document;
    private readonly DispatcherTimer _timer;
    private SyntaxTree? _tree;
    private ITextSourceVersion? _treeVersion;
    private int _generation;
    private bool _disposed;

    public ScriptAnalyzer(TextDocument document)
    {
        _document = document;
        Markers = new TextSegmentCollection<DiagnosticMarker>(document);
        _timer = new DispatcherTimer { Interval = Delay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            AnalyzeInBackground();
        };
        document.TextChanged += OnTextChanged;
        AnalyzeInBackground();
    }

    public TextSegmentCollection<DiagnosticMarker> Markers { get; }

    public event EventHandler? Analyzed;

    public int ErrorCount => Markers.Count(m => m.Diagnostic.Severity == DiagnosticSeverity.Error);

    public int WarningCount => Markers.Count(m => m.Diagnostic.Severity == DiagnosticSeverity.Warning);

    /// <summary>The background tree, if it matches the current text exactly.</summary>
    public SyntaxTree? FreshTree =>
        _tree is not null && _treeVersion is not null && _treeVersion.CompareAge(_document.Version) == 0 ? _tree : null;

    /// <summary>A tree for exactly the current text, parsing synchronously if the background one is stale.</summary>
    public SyntaxTree GetCurrentTree()
    {
        if (FreshTree is { } fresh)
            return fresh;

        _generation++; // Any background parse in flight is now outdated.
        var tree = SyntaxTree.Parse(_document.Text);
        Apply(tree, _document.Version);
        return tree;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _document.TextChanged -= OnTextChanged;
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _timer.Stop();
        _timer.Start();
    }

    private async void AnalyzeInBackground()
    {
        var version = _document.Version;
        var text = _document.Text;
        var generation = ++_generation;

        SyntaxTree tree;
        try
        {
            tree = await Task.Run(() => SyntaxTree.Parse(text));
        }
        catch (Exception ex)
        {
            // The parser is designed never to throw; if it does, keep the editor alive.
            Trace.WriteLine($"Background parse failed: {ex}");
            return;
        }

        if (!_disposed && generation == _generation)
            Apply(tree, version);
    }

    private void Apply(SyntaxTree tree, ITextSourceVersion version)
    {
        _tree = tree;
        _treeVersion = version;

        var current = _document.Version;
        var length = _document.TextLength;
        Markers.Clear();
        foreach (var diagnostic in tree.Diagnostics)
        {
            var start = Math.Min(version.MoveOffsetTo(current, diagnostic.Span.Start), length);
            var end = Math.Clamp(version.MoveOffsetTo(current, diagnostic.Span.End), start, length);
            Markers.Add(new DiagnosticMarker(start, end - start, diagnostic));
        }

        Analyzed?.Invoke(this, EventArgs.Empty);
    }
}
