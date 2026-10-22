using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using HoI4ModBuilder.Clausewitz.Classification;

namespace HoI4ModBuilder.Editor;

/// <summary>
/// Colours Clausewitz script line by line, so only visible lines are ever classified.
/// The lexer state at the start of each line (inside a multi-line string or not) is cached
/// and invalidated from the first edited line onwards.
/// </summary>
public sealed class ScriptColorizer : DocumentColorizingTransformer
{
    private readonly TextDocument _document;
    // Index i holds the state at the start of line i + 1; only a prefix is ever computed.
    private readonly List<LineState> _lineStartStates = [LineState.Default];
    private readonly List<ClassifiedSpan> _spans = [];
    private TextView? _textView;

    public ScriptColorizer(TextDocument document)
    {
        _document = document;
        document.Changed += OnDocumentChanged;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        _textView = CurrentContext.TextView;

        _spans.Clear();
        ScriptClassifier.ClassifyLine(_document.GetText(line), StartStateOf(line.LineNumber), _spans);

        foreach (var span in _spans)
        {
            if (EditorColors.For(span.Classification) is not { } brush)
                continue;
            ChangeLinePart(
                line.Offset + span.Start,
                line.Offset + span.End,
                element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    /// <summary>The lexer state at the start of a line (inside a multi-line string or not).</summary>
    public LineState StartStateOf(int lineNumber)
    {
        if (_lineStartStates.Count >= lineNumber)
            return _lineStartStates[lineNumber - 1];

        // Jumping to the end of a big file means scanning every line before it: take the text
        // once and use the allocation-free scanner rather than full classification per line.
        var text = _document.Text.AsSpan();
        var state = _lineStartStates[^1];
        for (var line = _document.GetLineByNumber(_lineStartStates.Count); _lineStartStates.Count < lineNumber; line = line.NextLine!)
        {
            state = ScriptClassifier.ScanLineState(text.Slice(line.Offset, line.Length), state);
            _lineStartStates.Add(state);
        }
        return state;
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        // States up to and including the edited line's start are unaffected.
        var editedLine = _document.GetLineByOffset(Math.Min(e.Offset, _document.TextLength)).LineNumber;
        if (_lineStartStates.Count > editedLine)
            _lineStartStates.RemoveRange(editedLine, _lineStartStates.Count - editedLine);

        // Quotes and inline-math brackets can change how every following line is coloured,
        // but the editor only redraws the edited lines by itself.
        if (_textView is not null && (ChangesLineState(e.InsertedText) || ChangesLineState(e.RemovedText)))
        {
            var textView = _textView;
            Dispatcher.UIThread.Post(() => textView.Redraw(), DispatcherPriority.Background);
        }
    }

    private static bool ChangesLineState(ITextSource text) =>
        text.TextLength > 0 && text.Text.AsSpan().IndexOfAny("\"[]\\") >= 0;
}
