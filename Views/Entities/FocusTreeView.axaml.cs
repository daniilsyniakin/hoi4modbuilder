using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Shapes;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using HoI4ModBuilder.ViewModels.Entities;
using HoI4ModBuilder.Views.Graph;

namespace HoI4ModBuilder.Views.Entities;

public partial class FocusTreeView : UserControl
{
    private const double DragThreshold = 4;

    private FocusNodeViewModel? _dragNode;
    private List<(FocusNodeViewModel Node, Point Start)> _dragGroup = [];
    private Point _dragStartWorld;
    private bool _dragging;
    private bool _linkDragging;
    private Point? _pointerWorld;

    public FocusTreeView()
    {
        InitializeComponent();
        World.AddHandler(PointerPressedEvent, OnNodePressed, RoutingStrategies.Bubble);
        World.AddHandler(PointerMovedEvent, OnNodeMoved, RoutingStrategies.Bubble);
        World.AddHandler(PointerReleasedEvent, OnNodeReleased, RoutingStrategies.Bubble);
        World.AddHandler(DoubleTappedEvent, OnNodeDoubleTapped, RoutingStrategies.Bubble);
        Surface.AddHandler(PointerMovedEvent, OnSurfacePointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        Surface.PointerExited += (_, _) =>
        {
            if (ViewModel is { } viewModel)
                viewModel.HoveredNode = null;
        };
        Surface.BackgroundClicked += OnBackgroundClicked;
        Surface.SelectionChanging += OnSelectionChanging;
        Surface.SelectionCompleted += (_, e) =>
        {
            if (e.Rect is { } rect)
                ViewModel?.SelectInRect(rect, e.Adds);
        };
        Surface.PointerExited += (_, _) => _pointerWorld = null;
        Surface.BackgroundDoubleClicked += OnBackgroundDoubleClicked;
        Surface.ZoomChanged += (_, _) => UpdateZoomText();
        KeyDown += OnKeyDown;
        UpdateZoomText();
    }

    private FocusTreeTabViewModel? ViewModel => DataContext as FocusTreeTabViewModel;

    private static FocusNodeViewModel? NodeOf(RoutedEventArgs e) => (e.Source as StyledElement)?.DataContext as FocusNodeViewModel;

    private void OnNodePressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } viewModel || NodeOf(e) is not { } node)
            return;
        var point = e.GetCurrentPoint(World);

        if (point.Properties.IsRightButtonPressed)
        {
            // Select it, then let the context menu open.
            viewModel.SelectedNode = node;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        Surface.Focus();
        if (viewModel.IsLinking)
        {
            viewModel.CompleteLink(node, alternative: e.KeyModifiers.HasFlag(KeyModifiers.Control));
            return;
        }

        // The handle under a focus: drag it onto another focus to make this one its prerequisite.
        if (e.Source is Ellipse { Classes: var classes } && classes.Contains("link-handle"))
        {
            viewModel.StartPrerequisiteLinkCommand.Execute(node);
            _linkDragging = true;
            e.Pointer.Capture(World);
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            viewModel.ToggleSelected(node);
            return;
        }

        // Dragging one of several selected focuses moves them all.
        viewModel.Click(node);
        _dragNode = node;
        _dragGroup = viewModel.Selection.Select(n => (n, new Point(n.X, n.Y))).ToList();
        _dragStartWorld = point.Position;
        _dragging = false;
        e.Pointer.Capture(World);
    }

    /// <summary>Tracks the hovered focus; while linking, the preview line follows the pointer.</summary>
    private void OnSurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;
        _pointerWorld = e.GetPosition(World);
        // While dragging, the dragged focus stays the "hovered" one.
        if (_dragNode is null)
            viewModel.HoveredNode = NodeOf(e);
        if (viewModel.IsLinking)
            viewModel.UpdateLinkPointer(e.GetPosition(World));
    }

    private void OnNodeMoved(object? sender, PointerEventArgs e)
    {
        if (_dragNode is not { } node)
            return;
        var delta = e.GetPosition(World) - _dragStartWorld;
        if (!_dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) < DragThreshold)
            return;
        _dragging = true;
        foreach (var (each, start) in _dragGroup)
        {
            each.X = start.X + delta.X;
            each.Y = start.Y + delta.Y;
        }
    }

    private void OnNodeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_linkDragging)
        {
            _linkDragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            if (ViewModel is not { } linking)
                return;
            var at = e.GetPosition(World);
            if (linking.Nodes.FirstOrDefault(n => new Rect(n.X, n.Y, n.Width, n.Height).Contains(at)) is { } target)
                linking.CompleteLink(target, alternative: e.KeyModifiers.HasFlag(KeyModifiers.Control));
            else
                linking.CancelLink();
            return;
        }
        if (_dragNode is not { } node)
            return;
        _dragNode = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (!_dragging || ViewModel is not { } viewModel)
            return;

        var (gridX, gridY) = viewModel.ToGrid(node.X, node.Y);
        var (dx, dy) = (gridX - node.GridX, gridY - node.GridY);
        if (dx == 0 && dy == 0)
        {
            foreach (var (each, start) in _dragGroup)
            {
                each.X = start.X;
                each.Y = start.Y;
            }
        }
        else
        {
            viewModel.MoveFocuses(_dragGroup.Select(g => g.Node).ToList(), dx, dy);
        }
        _dragGroup = [];
    }

    /// <summary>The frame being drawn on the canvas.</summary>
    private void OnSelectionChanging(object? sender, GraphSelectionEventArgs e)
    {
        if (e.Rect is not { } rect)
        {
            Band.IsVisible = false;
            return;
        }
        Canvas.SetLeft(Band, rect.X);
        Canvas.SetTop(Band, rect.Y);
        Band.Width = rect.Width;
        Band.Height = rect.Height;
        Band.IsVisible = true;
    }

    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (NodeOf(e) is not { } node)
            return;
        e.Handled = true;
        ViewModel?.OpenFocusInCodeCommand.Execute(node);
    }

    private void OnBackgroundClicked(object? sender, GraphBackgroundEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;
        viewModel.CancelLink();
        viewModel.SelectedNode = null;
    }

    private void OnBackgroundDoubleClicked(object? sender, GraphBackgroundEventArgs e) => AddFocusAt(e.World);

    private void OnAddFocus(object? sender, RoutedEventArgs e) =>
        AddFocusAt(Surface.ToWorld(new Point(Surface.Bounds.Width / 2, Surface.Bounds.Height / 2)));

    /// <summary>Adds a focus in the free cell nearest to a world point.</summary>
    private void AddFocusAt(Point world)
    {
        if (ViewModel is not { } viewModel)
            return;
        var (x, y) = viewModel.ToGrid(world.X - FocusTreeTabViewModel.NodeWidth / 2, world.Y - FocusTreeTabViewModel.NodeHeight / 2);
        while (viewModel.Nodes.Any(n => n.GridX == x && n.GridY == y))
            x++;
        viewModel.AddFocus(x, y);
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;
        if (e.Key == Key.Escape && viewModel.IsLinking)
        {
            viewModel.CancelLink();
            e.Handled = true;
            return;
        }
        // The rest only on the canvas: the inspector's text boxes have their own Delete and clipboard.
        if (!Surface.IsKeyboardFocusWithin)
            return;
        var ctrl = e.KeyModifiers == KeyModifiers.Control;
        switch (e.Key)
        {
            case Key.Delete when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                viewModel.DeleteSelectedCommand.Execute(null);
                break;
            case Key.A when ctrl:
                e.Handled = true;
                viewModel.SelectInRect(new Rect(0, 0, viewModel.CanvasWidth, viewModel.CanvasHeight), add: false);
                break;
            case Key.C when ctrl:
                e.Handled = true;
                if (viewModel.CopySelected() is { } text && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                    await clipboard.SetTextAsync(text);
                break;
            case Key.V when ctrl && FocusTreeTabViewModel.HasCopied:
                e.Handled = true;
                await viewModel.PasteAsync(PasteCell(viewModel));
                break;
        }
    }

    /// <summary>Where a paste goes: the cell under the pointer, or the middle of the view.</summary>
    private (int X, int Y) PasteCell(FocusTreeTabViewModel viewModel)
    {
        var world = _pointerWorld ?? Surface.ToWorld(new Point(Surface.Bounds.Width / 2, Surface.Bounds.Height / 2));
        return viewModel.ToGrid(world.X - FocusTreeTabViewModel.NodeWidth / 2, world.Y - FocusTreeTabViewModel.NodeHeight / 2);
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Surface.ZoomBy(1.25);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Surface.ZoomBy(1 / 1.25);

    private void OnFit(object? sender, RoutedEventArgs e) => Surface.Fit();

    private void UpdateZoomText() => ZoomText.Text = $"{Surface.Zoom * 100:0}%";
}
