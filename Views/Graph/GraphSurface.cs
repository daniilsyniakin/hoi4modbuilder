using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace HoI4ModBuilder.Views.Graph;

/// <summary>
/// An infinite canvas viewport: the child is the "world" (a canvas of nodes), shown through a
/// pan/zoom transform. Wheel zooms around the pointer, dragging the background pans. Nodes handle
/// their own pointer presses; presses that reach the surface pan or count as a background click.
/// </summary>
public class GraphSurface : Border
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 3;
    private const double ClickSlop = 4;

    /// <summary>The current pan/zoom; bound to the view model so it survives tab switches. Null asks for a fit.</summary>
    public static readonly StyledProperty<Matrix?> ViewTransformProperty =
        AvaloniaProperty.Register<GraphSurface, Matrix?>(nameof(ViewTransform),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly RoutedEvent<GraphBackgroundEventArgs> BackgroundClickedEvent =
        RoutedEvent.Register<GraphSurface, GraphBackgroundEventArgs>(nameof(BackgroundClicked), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<GraphBackgroundEventArgs> BackgroundDoubleClickedEvent =
        RoutedEvent.Register<GraphSurface, GraphBackgroundEventArgs>(nameof(BackgroundDoubleClicked), RoutingStrategies.Bubble);

    private Matrix _matrix = Matrix.Identity;
    private Point? _panStart;
    private Matrix _panStartMatrix;
    private bool _panned;
    private bool _applying;
    private bool _fitPending = true;

    public GraphSurface()
    {
        ClipToBounds = true;
        Focusable = true;
        DoubleTapped += OnDoubleTapped;
    }

    public Matrix? ViewTransform
    {
        get => GetValue(ViewTransformProperty);
        set => SetValue(ViewTransformProperty, value);
    }

    public event EventHandler<GraphBackgroundEventArgs>? BackgroundClicked
    {
        add => AddHandler(BackgroundClickedEvent, value);
        remove => RemoveHandler(BackgroundClickedEvent, value);
    }

    public event EventHandler<GraphBackgroundEventArgs>? BackgroundDoubleClicked
    {
        add => AddHandler(BackgroundDoubleClickedEvent, value);
        remove => RemoveHandler(BackgroundDoubleClickedEvent, value);
    }

    public double Zoom => _matrix.M11;

    /// <summary>Raised when <see cref="Zoom"/> changes, for a zoom read-out.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>Surface coordinates → world coordinates.</summary>
    public Point ToWorld(Point surfacePoint) =>
        _matrix.TryInvert(out var inverse) ? inverse.Transform(surfacePoint) : surfacePoint;

    public void ZoomBy(double factor) => ZoomAround(new Point(Bounds.Width / 2, Bounds.Height / 2), factor);

    /// <summary>Shows the whole world, at most at 100 %.</summary>
    public void Fit()
    {
        if (Child is not { } child || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _fitPending = true;
            return;
        }
        var size = child.DesiredSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            _fitPending = true;
            return;
        }
        _fitPending = false;
        var zoom = Math.Clamp(Math.Min(Bounds.Width / size.Width, Bounds.Height / size.Height), MinZoom, 1);
        var x = (Bounds.Width - size.Width * zoom) / 2;
        var y = Math.Max(0, (Bounds.Height - size.Height * zoom) / 2);
        Apply(Matrix.CreateScale(zoom, zoom) * Matrix.CreateTranslation(x, y));
    }

    /// <summary>Scrolls so that a world point is in the middle, keeping the zoom.</summary>
    public void CenterOn(Point world)
    {
        var screen = _matrix.Transform(world);
        Apply(_matrix * Matrix.CreateTranslation(Bounds.Width / 2 - screen.X, Bounds.Height / 2 - screen.Y));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewTransformProperty && !_applying)
        {
            if (ViewTransform is { } matrix)
            {
                _fitPending = false;
                Apply(matrix);
            }
            else
            {
                _fitPending = true;
                Dispatcher.UIThread.Post(Fit, DispatcherPriority.Loaded);
            }
        }
        else if (change.Property == ChildProperty && Child is { } child)
        {
            child.RenderTransformOrigin = RelativePoint.TopLeft;
            child.RenderTransform = new MatrixTransform(_matrix);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // The world is as big as it wants to be; the surface takes whatever it is given.
        Child?.Measure(Size.Infinity);
        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(Child.DesiredSize));
        if (_fitPending)
            Dispatcher.UIThread.Post(() => { if (_fitPending) Fit(); }, DispatcherPriority.Loaded);
        return finalSize;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAround(e.GetPosition(this), Math.Pow(1.15, e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        if (e.Handled || !(point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed))
            return;
        _panStart = point.Position;
        _panStartMatrix = _matrix;
        _panned = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_panStart is not { } start)
            return;
        var delta = e.GetPosition(this) - start;
        if (!_panned && Math.Abs(delta.X) + Math.Abs(delta.Y) < ClickSlop)
            return;
        _panned = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        Apply(_panStartMatrix * Matrix.CreateTranslation(delta.X, delta.Y));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panStart is null)
            return;
        _panStart = null;
        Cursor = null;
        e.Pointer.Capture(null);
        if (!_panned && e.InitialPressMouseButton == MouseButton.Left)
            RaiseEvent(new GraphBackgroundEventArgs(BackgroundClickedEvent, ToWorld(e.GetPosition(this))));
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled || !ReferenceEquals(e.Source, this) && (e.Source as Control)?.DataContext != DataContext)
            return;
        RaiseEvent(new GraphBackgroundEventArgs(BackgroundDoubleClickedEvent, ToWorld(e.GetPosition(this))));
    }

    private void ZoomAround(Point center, double factor)
    {
        var zoom = Math.Clamp(_matrix.M11 * factor, MinZoom, MaxZoom);
        factor = zoom / _matrix.M11;
        Apply(_matrix * Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateScale(factor, factor)
              * Matrix.CreateTranslation(center.X, center.Y));
    }

    private void Apply(Matrix matrix)
    {
        _matrix = matrix;
        if (Child is { } child)
            child.RenderTransform = new MatrixTransform(matrix);
        _applying = true;
        try
        {
            SetCurrentValue(ViewTransformProperty, matrix);
        }
        finally
        {
            _applying = false;
        }
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A click on empty space of a <see cref="GraphSurface"/>, in world coordinates.</summary>
public sealed class GraphBackgroundEventArgs(RoutedEvent routedEvent, Point world) : RoutedEventArgs(routedEvent)
{
    public Point World { get; } = world;
}
