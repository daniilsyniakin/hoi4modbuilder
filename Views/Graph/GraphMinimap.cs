using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HoI4ModBuilder.ViewModels.Entities;

namespace HoI4ModBuilder.Views.Graph;

/// <summary>
/// The whole graph in small with the visible part framed; clicking or dragging on it moves the view.
/// Shown only when the graph doesn't fit the view.
/// </summary>
public class GraphMinimap : Control
{
    private const double MaxSide = 200;
    private const double Padding = 6;

    private static readonly IBrush Fill = new ImmutableSolidColorBrush(Color.Parse("#E6202226"));
    private static readonly IPen Outline = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#393B40")), 1);
    private static readonly IBrush NodeBrush = new ImmutableSolidColorBrush(Color.Parse("#8C8577"));
    private static readonly IBrush SelectedBrush = new ImmutableSolidColorBrush(Color.Parse("#3574F0"));
    private static readonly IPen ViewPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#DFE1E5")), 1.2);
    private static readonly IBrush ViewBrush = new ImmutableSolidColorBrush(Color.Parse("#14FFFFFF"));

    public static readonly StyledProperty<GraphSurface?> SurfaceProperty =
        AvaloniaProperty.Register<GraphMinimap, GraphSurface?>(nameof(Surface));

    public static readonly StyledProperty<IEnumerable?> NodesProperty =
        AvaloniaProperty.Register<GraphMinimap, IEnumerable?>(nameof(Nodes));

    /// <summary>Drawn under the nodes, stretched over the whole world (e.g. the map itself).</summary>
    public static readonly StyledProperty<IImage?> PictureProperty =
        AvaloniaProperty.Register<GraphMinimap, IImage?>(nameof(Picture));

    public static readonly StyledProperty<double> WorldWidthProperty =
        AvaloniaProperty.Register<GraphMinimap, double>(nameof(WorldWidth));

    public static readonly StyledProperty<double> WorldHeightProperty =
        AvaloniaProperty.Register<GraphMinimap, double>(nameof(WorldHeight));

    private bool _dragging;

    static GraphMinimap()
    {
        AffectsRender<GraphMinimap>(NodesProperty, PictureProperty, WorldWidthProperty, WorldHeightProperty);
        AffectsMeasure<GraphMinimap>(WorldWidthProperty, WorldHeightProperty);
    }

    public GraphMinimap()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        ClipToBounds = true;
    }

    public GraphSurface? Surface
    {
        get => GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public IImage? Picture
    {
        get => GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    public IEnumerable? Nodes
    {
        get => GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public double WorldWidth
    {
        get => GetValue(WorldWidthProperty);
        set => SetValue(WorldWidthProperty, value);
    }

    public double WorldHeight
    {
        get => GetValue(WorldHeightProperty);
        set => SetValue(WorldHeightProperty, value);
    }

    /// <summary>World units per minimap pixel.</summary>
    private double Scale => Math.Max(WorldWidth, WorldHeight) <= 0 ? 1 : Math.Max(WorldWidth, WorldHeight) / (MaxSide - 2 * Padding);

    protected override Size MeasureOverride(Size availableSize) =>
        WorldWidth <= 0 || WorldHeight <= 0
            ? default
            : new Size(WorldWidth / Scale + 2 * Padding, WorldHeight / Scale + 2 * Padding);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SurfaceProperty)
        {
            if (change.OldValue is GraphSurface old)
            {
                old.ZoomChanged -= OnViewChanged;
                old.PropertyChanged -= OnSurfacePropertyChanged;
            }
            if (change.NewValue is GraphSurface surface)
            {
                surface.ZoomChanged += OnViewChanged;
                surface.PropertyChanged += OnSurfacePropertyChanged;
            }
        }
        else if (change.Property == NodesProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldCollection)
                oldCollection.CollectionChanged -= OnNodesChanged;
            if (change.NewValue is INotifyCollectionChanged newCollection)
                newCollection.CollectionChanged += OnNodesChanged;
            Watch(change.OldValue as IEnumerable, false);
            Watch(change.NewValue as IEnumerable, true);
        }
        UpdateVisibility();
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Watch(e.OldItems, false);
        Watch(e.NewItems, true);
        if (e.Action == NotifyCollectionChangedAction.Reset)
            Watch(Nodes, true);
        InvalidateVisual();
    }

    // Moving and selecting nodes redraws.
    private void Watch(IEnumerable? nodes, bool watch)
    {
        foreach (var node in nodes?.OfType<GraphNodeViewModel>() ?? [])
        {
            node.PropertyChanged -= OnNodeChanged;
            if (watch)
                node.PropertyChanged += OnNodeChanged;
        }
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private void OnViewChanged(object? sender, EventArgs e)
    {
        UpdateVisibility();
        InvalidateVisual();
    }

    private void OnSurfacePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty)
            OnViewChanged(sender, e);
    }

    /// <summary>Only for graphs bigger than the view.</summary>
    private void UpdateVisibility()
    {
        if (Surface is not { } surface || WorldWidth <= 0)
        {
            IsVisible = false;
            return;
        }
        var zoom = surface.Zoom;
        IsVisible = WorldWidth * zoom > surface.Bounds.Width + 1 || WorldHeight * zoom > surface.Bounds.Height + 1;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(Fill, Outline, bounds, 4, 4);
        var scale = Scale;
        Point ToMap(double x, double y) => new(Padding + x / scale, Padding + y / scale);
        if (Picture is { } picture)
            context.DrawImage(picture, new Rect(picture.Size), new Rect(ToMap(0, 0), ToMap(WorldWidth, WorldHeight)));

        foreach (var node in Nodes?.OfType<GraphNodeViewModel>() ?? [])
        {
            var topLeft = ToMap(node.X, node.Y);
            var size = new Size(Math.Max(2, node.Width / scale * 0.8), Math.Max(2, node.Height / scale * 0.6));
            context.DrawRectangle(node.IsSelected ? SelectedBrush : NodeBrush, null, new Rect(topLeft, size), 1, 1);
        }

        if (Surface is { } surface && surface.ViewMatrix.TryInvert(out var inverse))
        {
            var from = inverse.Transform(new Point(0, 0));
            var to = inverse.Transform(new Point(surface.Bounds.Width, surface.Bounds.Height));
            var view = new Rect(ToMap(from.X, from.Y), ToMap(to.X, to.Y)).Intersect(bounds.Deflate(1));
            context.DrawRectangle(ViewBrush, ViewPen, view, 2, 2);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        CenterOn(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
            CenterOn(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void CenterOn(Point point) =>
        Surface?.CenterOn(new Point((point.X - Padding) * Scale, (point.Y - Padding) * Scale));
}
