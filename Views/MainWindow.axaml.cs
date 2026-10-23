using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Indentation;
using AvaloniaEdit.Rendering;
using HoI4ModBuilder.Clausewitz.Classification;
using HoI4ModBuilder.Editor;
using HoI4ModBuilder.Game.Imaging;
using HoI4ModBuilder.Models;
using HoI4ModBuilder.ViewModels;

namespace HoI4ModBuilder.Views;

public partial class MainWindow : Window
{
    // Colorizers cache per-line lexer state, so each document keeps its own for as long as it lives.
    private readonly ConditionalWeakTable<TextDocument, ScriptColorizer> _colorizers = new();
    private readonly DiagnosticRenderer _diagnosticRenderer = new();
    private readonly CompletionController _completion;
    private MainWindowViewModel? _viewModel;
    private EditorTabViewModel? _shownTab;
    private ScriptColorizer? _activeColorizer;
    private string? _hoverKey;
    private GridLength _projectPanelWidth = new(280);
    private PixelPoint _normalPosition;
    private Size _normalSize;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureEditor();
        _completion = new CompletionController(Editor)
        {
            Vocabulary = () => _viewModel?.Vocabulary,
            Assets = () => _viewModel?.Assets,
        };

        ProjectTreeView.DoubleTapped += OnTreeDoubleTapped;
        ProjectTreeView.KeyDown += OnTreeKeyDown;
        // Focus trees have children, so their row handles the double tap (expand) before it bubbles up.
        ContentTreeView.AddHandler(DoubleTappedEvent, OnContentTreeDoubleTapped, handledEventsToo: true);
        ContentTreeView.KeyDown += OnContentTreeKeyDown;
        TabStrip.PointerReleased += OnTabStripPointerReleased;
        ProblemsList.DoubleTapped += (_, _) => _viewModel?.OpenProblemCommand.Execute(ProblemsList.SelectedItem as ProblemRowViewModel);
        ProblemsList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                _viewModel?.OpenProblemCommand.Execute(ProblemsList.SelectedItem as ProblemRowViewModel);
        };
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
        Deactivated += (_, _) =>
        {
            _viewModel?.CloseSwitcher();
            _viewModel?.Search.Close();
        };
        SwitcherList.Tapped += (_, _) => _viewModel?.CommitSwitcher();
        SearchResults.Tapped += (_, _) => _viewModel?.Search.Commit();
        SearchBox.KeyDown += OnSearchKeyDown;
        // A click anywhere else closes search everywhere.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_viewModel?.Search.IsOpen == true && !(e.Source is Visual source && SearchOverlay.IsVisualAncestorOf(source)))
                _viewModel.Search.Close();
        }, RoutingStrategies.Tunnel);
        // Maximized bounds are not worth remembering: restoring should bring back the normal ones.
        PositionChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal)
                _normalPosition = Position;
        };
        Resized += (_, _) =>
        {
            if (WindowState == WindowState.Normal)
                _normalSize = ClientSize;
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.NavigationRequested -= OnNavigationRequested;
            _viewModel.Search.PropertyChanged -= OnSearchPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.NavigationRequested += OnNavigationRequested;
            _viewModel.Search.PropertyChanged += OnSearchPropertyChanged;
        }

        ShowTab(_viewModel?.ActiveTextTab);
        if (_viewModel?.Settings.Settings.Window is { } layout)
            ApplyLayout(layout);
        ApplyProjectPanelVisibility();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _viewModel is null)
            return;
        if (_shownTab is not null)
            _shownTab.SavedCaretOffset = Editor.CaretOffset;
        _viewModel.Settings.Settings.Window = CurrentLayout();
        _viewModel.SaveSession();
        _viewModel.Dispose();
    }

    private void ApplyLayout(WindowLayout layout)
    {
        if (layout.ProjectPanelWidth >= 160)
        {
            _projectPanelWidth = new GridLength(layout.ProjectPanelWidth);
            WorkspaceGrid.ColumnDefinitions[0].Width = _projectPanelWidth;
        }
        if (layout.Width < MinWidth || layout.Height < MinHeight)
            return;
        Width = layout.Width;
        Height = layout.Height;
        _normalSize = new Size(layout.Width, layout.Height);
        // Only where a screen still is (a monitor may have been unplugged since).
        var position = new PixelPoint(layout.X, layout.Y);
        if (Screens.All.Any(screen => screen.WorkingArea.Contains(position + new PixelPoint(40, 20))))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = position;
            _normalPosition = position;
        }
        if (layout.Maximized)
            WindowState = WindowState.Maximized;
    }

    private WindowLayout CurrentLayout()
    {
        var column = WorkspaceGrid.ColumnDefinitions[0].Width;
        var size = _normalSize == default ? ClientSize : _normalSize;
        return new WindowLayout
        {
            X = (_normalPosition == default ? Position : _normalPosition).X,
            Y = (_normalPosition == default ? Position : _normalPosition).Y,
            Width = size.Width,
            Height = size.Height,
            Maximized = WindowState == WindowState.Maximized,
            ProjectPanelWidth = column.Value > 0 ? column.Value : _projectPanelWidth.Value,
        };
    }

    private void ConfigureEditor()
    {
        var options = Editor.Options;
        options.ConvertTabsToSpaces = false; // Paradox scripts are tab-indented.
        options.IndentationSize = 4;
        options.HighlightCurrentLine = true;
        options.EnableHyperlinks = false;
        options.EnableEmailHyperlinks = false;
        options.AllowScrollBelowDocument = true;

        Editor.TextArea.SelectionBrush = new ImmutableSolidColorBrush(Color.Parse("#214283"));
        Editor.TextArea.TextView.CurrentLineBackground = new ImmutableSolidColorBrush(Color.Parse("#26282E"));
        Editor.TextArea.TextView.CurrentLineBorder = new ImmutablePen(Colors.Transparent.ToUInt32(), 0);
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaretPosition();

        // Give the gutter some air, JetBrains style.
        foreach (var margin in Editor.TextArea.LeftMargins.OfType<LineNumberMargin>())
            margin.Margin = new Thickness(10, 0, 14, 0);

        Editor.TextArea.TextView.BackgroundRenderers.Add(_diagnosticRenderer);
        Editor.TextArea.TextEntered += (_, e) =>
        {
            if (_shownTab is { IsScript: true } && e.Text is { } text)
                ScriptIndentationStrategy.OnTextEntered(Editor, text);
        };
        Editor.TextArea.TextView.PointerMoved += OnEditorPointerMoved;
        Editor.TextArea.TextView.PointerExited += (_, _) =>
        {
            ToolTip.SetIsOpen(Editor.TextArea.TextView, false);
            _hoverKey = null;
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.ActiveTab):
                ShowTab(_viewModel?.ActiveTextTab);
                break;
            case nameof(MainWindowViewModel.IsProjectPanelVisible):
                ApplyProjectPanelVisibility();
                break;
        }
    }

    /// <summary>
    /// A single editor control is reused for all tabs, so caret and scroll position are
    /// stashed on the outgoing tab and restored on the incoming one.
    /// </summary>
    private void ShowTab(EditorTabViewModel? tab)
    {
        if (tab == _shownTab)
            return;

        if (_shownTab is not null)
        {
            _shownTab.SavedCaretOffset = Editor.CaretOffset;
            _shownTab.SavedScrollOffset = new Vector(Editor.HorizontalOffset, Editor.VerticalOffset);
        }

        _completion.Close();
        AttachLanguageServices(tab);
        _shownTab = tab;
        if (tab is null)
        {
            Editor.Document = new TextDocument();
            return;
        }

        Editor.Document = tab.Document;
        Editor.CaretOffset = Math.Min(tab.SavedCaretOffset, tab.Document.TextLength);
        var scroll = tab.SavedScrollOffset;
        Dispatcher.UIThread.Post(() =>
        {
            Editor.ScrollToHorizontalOffset(scroll.X);
            Editor.ScrollToVerticalOffset(scroll.Y);
            Editor.TextArea.Focus();
        }, DispatcherPriority.Loaded);
        UpdateCaretPosition();
    }

    /// <summary>Puts the caret at <paramref name="offset"/> and scrolls it into view, e.g. for go-to-definition.</summary>
    private void OnNavigationRequested(EditorTabViewModel tab, int offset)
    {
        if (tab != _shownTab)
            return;
        Editor.CaretOffset = Math.Min(offset, Editor.Document.TextLength);
        var location = Editor.Document.GetLocation(Editor.CaretOffset);
        // After ShowTab's own scroll restore, which is queued at the same priority.
        Dispatcher.UIThread.Post(() =>
        {
            Editor.ScrollTo(location.Line, location.Column);
            Editor.TextArea.Focus();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Swaps highlighting, squiggles and completion over to the newly shown tab.</summary>
    private void AttachLanguageServices(EditorTabViewModel? tab)
    {
        var lineTransformers = Editor.TextArea.TextView.LineTransformers;
        if (_activeColorizer is not null)
            lineTransformers.Remove(_activeColorizer);
        if (_shownTab?.Analyzer is { } oldAnalyzer)
            oldAnalyzer.Analyzed -= OnAnalyzed;

        _activeColorizer = tab is { IsScript: true }
            ? _colorizers.GetValue(tab.Document, document => new ScriptColorizer(document))
            : null;
        if (_activeColorizer is not null)
            lineTransformers.Add(_activeColorizer);

        _diagnosticRenderer.Analyzer = tab?.Analyzer;
        if (tab?.Analyzer is { } analyzer)
            analyzer.Analyzed += OnAnalyzed;
        _completion.Tab = tab;
        Editor.TextArea.IndentationStrategy = tab is { IsScript: true }
            ? ScriptIndentation
            : new DefaultIndentationStrategy();
    }

    private static readonly ScriptIndentationStrategy ScriptIndentation = new();

    private void OnAnalyzed(object? sender, EventArgs e) =>
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);

    /// <summary>
    /// Hover tooltips: the diagnostic under the pointer, otherwise a preview of the sprite
    /// (<c>GFX_…</c>) or texture path under it.
    /// </summary>
    private void OnEditorPointerMoved(object? sender, PointerEventArgs e)
    {
        var textView = Editor.TextArea.TextView;
        string? key = null;
        Func<Task<object>>? buildTip = null;

        if (_shownTab is not null
            && textView.GetPosition(e.GetPosition(textView) + textView.ScrollOffset) is { } position)
        {
            var offset = Editor.Document.GetOffset(position.Location);
            if (_shownTab.Analyzer?.Markers.FindSegmentsContaining(offset).FirstOrDefault() is { } marker)
            {
                key = "diagnostic:" + marker.StartOffset + marker.Diagnostic.Message;
                buildTip = () => Task.FromResult<object>(marker.Diagnostic.Message);
            }
            else if (FindAssetAt(offset) is { } asset)
            {
                (key, buildTip) = asset;
            }
        }

        if (key == _hoverKey)
            return;

        _hoverKey = key;
        ToolTip.SetIsOpen(textView, false);
        if (buildTip is not null)
            _ = ShowTipAsync(key!, buildTip);
    }

    /// <summary>Opens the tooltip once its content is ready, unless the pointer has moved on.</summary>
    private async Task ShowTipAsync(string key, Func<Task<object>> buildTip)
    {
        var tip = await buildTip();
        if (_hoverKey != key)
            return;
        var textView = Editor.TextArea.TextView;
        ToolTip.SetTip(textView, tip);
        ToolTip.SetIsOpen(textView, true);
    }

    /// <summary>A sprite name or texture path at <paramref name="offset"/>, with a preview factory.</summary>
    private (string Key, Func<Task<object>> Build)? FindAssetAt(int offset)
    {
        if (_activeColorizer is null || _viewModel?.Assets is not { } assets)
            return null;

        var line = Editor.Document.GetLineByOffset(offset);
        var text = Editor.Document.GetText(line);
        var spans = new List<ClassifiedSpan>();
        ScriptClassifier.ClassifyLine(text, _activeColorizer.StartStateOf(line.LineNumber), spans);

        var column = offset - line.Offset;
        var span = spans.FirstOrDefault(s => s.Start <= column && column < s.End);
        if (span.Length == 0 || span.Classification is not (ScriptClassification.Value or ScriptClassification.Key
                or ScriptClassification.String or ScriptClassification.Tag))
            return null;

        var word = text.Substring(span.Start, span.Length).Trim('"');
        if (assets.Sprites.Find(word) is { } sprite)
            return ("sprite:" + sprite.Name, async () => await AssetPreview.ForSpriteAsync(assets, sprite));
        if (TextureDecoder.IsImage(word) && assets.FileSystem.ResolveTexture(word) is { } file)
            return ("texture:" + file.FullPath, async () => await AssetPreview.ForTextureAsync(assets, file));
        return null;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        TrackDoubleShift(e, down: true);
        // Alt+F7: usages of the word under the caret in the text editor, else of the selected object.
        if (e.Key == Key.F7 && e.KeyModifiers == KeyModifiers.Alt && _viewModel is not null)
        {
            e.Handled = true;
            _viewModel.FindUsages(Editor.TextArea.IsKeyboardFocusWithin ? WordAtCaret() : null);
            return;
        }
        // Ctrl+Tab before the text editor, which would indent.
        if (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _viewModel is not null)
        {
            e.Handled = true;
            _viewModel.MoveSwitcher(back: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            return;
        }
        if (_viewModel?.IsSwitching == true)
        {
            e.Handled = true;
            switch (e.Key)
            {
                case Key.Escape:
                    _viewModel.CloseSwitcher();
                    break;
                case Key.Enter:
                    _viewModel.CommitSwitcher();
                    break;
                case Key.Down or Key.Right:
                    _viewModel.MoveSwitcher(back: false);
                    break;
                case Key.Up or Key.Left:
                    _viewModel.MoveSwitcher(back: true);
                    break;
            }
            return;
        }
        if (e.Key != Key.F2 || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift) || _viewModel is not { } viewModel)
            return;
        e.Handled = true;
        var plain = e.KeyModifiers == KeyModifiers.None;

        // F2 renames with all references, Shift+F2 only the definition: in the content tree and
        // in graphical editors. In the text editor they still go to the next / previous problem.
        if (ContentTreeView.IsKeyboardFocusWithin
            && viewModel.ProjectTree.SelectedContentNode is { Kind: { } kind, HasLocation: true } node)
            _ = viewModel.RenameAsync(kind, node.Name, plain);
        else if (viewModel.ActiveEntityTab?.RenameTarget is { } target)
            _ = viewModel.RenameAsync(target.Kind, target.Id, plain);
        else
            GoToProblem(forward: plain);
    }

    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchEverywhereViewModel.IsOpen) && _viewModel?.Search.IsOpen == true)
            Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Loaded);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel?.Search is not { } search)
            return;
        switch (e.Key)
        {
            case Key.Down:
                search.Move(1);
                break;
            case Key.Up:
                search.Move(-1);
                break;
            case Key.PageDown:
                search.Move(10);
                break;
            case Key.PageUp:
                search.Move(-10);
                break;
            case Key.Enter:
                search.Commit();
                break;
            case Key.Escape:
                search.Close();
                break;
            default:
                return;
        }
        e.Handled = true;
        if (search.Selected is { } selected)
            SearchResults.ScrollIntoView(selected);
    }

    // Double Shift: two Shift taps with nothing pressed in between, close together.
    private static readonly TimeSpan DoubleShiftInterval = TimeSpan.FromMilliseconds(400);
    private bool _shiftAlone;
    private DateTime _lastShiftTap;

    private void TrackDoubleShift(KeyEventArgs e, bool down)
    {
        var isShift = e.Key is Key.LeftShift or Key.RightShift;
        if (down)
        {
            _shiftAlone = isShift && (e.KeyModifiers & ~KeyModifiers.Shift) == 0;
            if (!isShift)
                _lastShiftTap = default;
            return;
        }
        if (!isShift || !_shiftAlone)
            return;
        _shiftAlone = false;
        var now = DateTime.UtcNow;
        if (now - _lastShiftTap <= DoubleShiftInterval && _viewModel is not null)
        {
            _lastShiftTap = default;
            _viewModel.SearchEverywhereCommand.Execute(null);
        }
        else
        {
            _lastShiftTap = now;
        }
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        TrackDoubleShift(e, down: false);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl && _viewModel?.IsSwitching == true)
        {
            e.Handled = true;
            _viewModel.CommitSwitcher();
        }
    }

    /// <summary>The id under the caret: letters, digits and <c>_ . : @ -</c>.</summary>
    private string? WordAtCaret()
    {
        if (_shownTab is null)
            return null;
        var document = Editor.Document;
        var line = document.GetLineByOffset(Editor.CaretOffset);
        var text = document.GetText(line);
        var column = Editor.CaretOffset - line.Offset;
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '.' or ':' or '@' or '-';
        var start = column;
        while (start > 0 && IsWordChar(text[start - 1]))
            start--;
        var end = column;
        while (end < text.Length && IsWordChar(text[end]))
            end++;
        return end > start ? text[start..end].Trim('.', ':') : null;
    }

    private void OnProblemsClick(object? sender, RoutedEventArgs e) => GoToProblem(forward: true);

    private void GoToProblem(bool forward)
    {
        if (_shownTab?.Analyzer is not { } analyzer || analyzer.Markers.Count == 0)
            return;

        var caret = Editor.CaretOffset;
        var markers = analyzer.Markers.OrderBy(m => m.StartOffset).ToList();
        var target = forward
            ? markers.FirstOrDefault(m => m.StartOffset > caret) ?? markers[0]
            : markers.LastOrDefault(m => m.StartOffset < caret) ?? markers[^1];

        Editor.CaretOffset = target.StartOffset;
        Editor.TextArea.Caret.BringCaretToView();
        Editor.TextArea.Focus();
        _viewModel!.StatusMessage = target.Diagnostic.Message;
    }

    private void UpdateCaretPosition()
    {
        if (_shownTab is null)
            return;
        var caret = Editor.TextArea.Caret;
        _shownTab.CaretLine = caret.Line;
        _shownTab.CaretColumn = caret.Column;
    }

    private void ApplyProjectPanelVisibility()
    {
        var visible = _viewModel?.IsProjectPanelVisible ?? true;
        var column = WorkspaceGrid.ColumnDefinitions[0];
        if (visible)
        {
            column.Width = _projectPanelWidth;
            column.MinWidth = 160;
        }
        else
        {
            if (column.Width.Value > 0)
                _projectPanelWidth = column.Width;
            column.MinWidth = 0;
            column.Width = new GridLength(0);
        }
        ProjectPanel.IsVisible = visible;
        ProjectSplitter.IsVisible = visible;
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Folders toggle themselves on double click; only files need handling.
        if ((e.Source as Control)?.DataContext is FileNodeViewModel { IsDirectory: false } node)
            _viewModel?.OpenNodeCommand.Execute(node);
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel?.ProjectTree.SelectedNode is not { } node)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                _viewModel.OpenNodeCommand.Execute(node);
                e.Handled = true;
                break;
            case Key.Delete when _viewModel.DeleteNodeCommand.CanExecute(node):
                _viewModel.DeleteNodeCommand.Execute(node);
                e.Handled = true;
                break;
        }
    }

    private void OnContentTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Folders toggle themselves; entities open in their graphical editor.
        if ((e.Source as Control)?.DataContext is ContentNodeViewModel { HasLocation: true } node)
            _viewModel?.OpenContentNodeCommand.Execute(node);
    }

    private void OnContentTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel?.ProjectTree.SelectedContentNode is { } node)
        {
            _viewModel.OpenContentNodeCommand.Execute(node);
            e.Handled = true;
        }
    }

    private void OnTabStripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Middle click closes a tab, as in browsers and IDEs.
        if (e.InitialPressMouseButton == MouseButton.Middle
            && (e.Source as Control)?.DataContext is DocumentTabViewModel tab)
        {
            _viewModel?.CloseTab(tab);
            e.Handled = true;
        }
    }
}
