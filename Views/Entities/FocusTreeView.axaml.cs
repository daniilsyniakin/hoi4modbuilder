using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HoI4ModBuilder.ViewModels.Entities;
using HoI4ModBuilder.Views.Graph;

namespace HoI4ModBuilder.Views.Entities;

public partial class FocusTreeView : UserControl
{
    private const double DragThreshold = 4;

    private FocusNodeViewModel? _dragNode;
    private Point _dragStartWorld;
    private Point _dragStartNode;
    private bool _dragging;

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
            viewModel.CompleteLink(node);
            return;
        }

        viewModel.SelectedNode = node;
        _dragNode = node;
        _dragStartWorld = point.Position;
        _dragStartNode = new Point(node.X, node.Y);
        _dragging = false;
        e.Pointer.Capture(World);
    }

    /// <summary>Tracks the hovered focus; while linking, the preview line follows the pointer.</summary>
    private void OnSurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;
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
        node.X = _dragStartNode.X + delta.X;
        node.Y = _dragStartNode.Y + delta.Y;
    }

    private void OnNodeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragNode is not { } node)
            return;
        _dragNode = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (!_dragging || ViewModel is not { } viewModel)
            return;

        var (gridX, gridY) = viewModel.ToGrid(node.X, node.Y);
        if (gridX == node.GridX && gridY == node.GridY)
        {
            node.X = _dragStartNode.X;
            node.Y = _dragStartNode.Y;
        }
        else
        {
            viewModel.MoveFocus(node, gridX, gridY);
        }
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

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel is { IsLinking: true } viewModel)
        {
            viewModel.CancelLink();
            e.Handled = true;
        }
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Surface.ZoomBy(1.25);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Surface.ZoomBy(1 / 1.25);

    private void OnFit(object? sender, RoutedEventArgs e) => Surface.Fit();

    private void UpdateZoomText() => ZoomText.Text = $"{Surface.Zoom * 100:0}%";
}
