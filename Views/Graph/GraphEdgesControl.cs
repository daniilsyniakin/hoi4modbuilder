using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HoI4ModBuilder.ViewModels.Entities;

namespace HoI4ModBuilder.Views.Graph;

/// <summary>Draws the connections of a graph behind its nodes, in world coordinates.</summary>
public class GraphEdgesControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<GraphEdge>?> EdgesProperty =
        AvaloniaProperty.Register<GraphEdgesControl, IReadOnlyList<GraphEdge>?>(nameof(Edges));

    private static readonly ImmutableSolidColorBrush LineBrush = new ImmutableSolidColorBrush(Color.Parse("#C9B37E"));
    private static readonly ImmutableSolidColorBrush ExclusiveBrush = new ImmutableSolidColorBrush(Color.Parse("#F75464"));
    private static readonly ImmutableSolidColorBrush FlowBrush = new ImmutableSolidColorBrush(Color.Parse("#548AF7"));

    private static readonly IPen SolidPen = new ImmutablePen(LineBrush, 2.5);
    private static readonly IPen DashedPen = new ImmutablePen(LineBrush, 2.5, new ImmutableDashStyle([3, 2], 0));
    private static readonly IPen ExclusivePen = new ImmutablePen(ExclusiveBrush, 2.5);
    private static readonly IPen FlowPen = new ImmutablePen(FlowBrush, 2);

    // Relative positions are layout scaffolding, not game logic: quiet enough not to compete with prerequisites.
    private static readonly ImmutableSolidColorBrush RelativeBrush = new(Color.Parse("#7F8BA3"), 0.55);
    private static readonly IPen RelativePen = new ImmutablePen(RelativeBrush, 1.5, new ImmutableDashStyle([4, 3], 0));
    private static readonly IPen RelativeRingPen = new ImmutablePen(RelativeBrush, 1.5);
    private static readonly IPen PreviewPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#E0A15A")), 2,
        new ImmutableDashStyle([4, 3], 0));

    static GraphEdgesControl()
    {
        AffectsRender<GraphEdgesControl>(EdgesProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<GraphEdgesControl>(false);
    }

    public IReadOnlyList<GraphEdge>? Edges
    {
        get => GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Edges is not { } edges)
            return;
        foreach (var edge in edges)
        {
            if (edge.Points.Count < 2)
                continue;
            switch (edge.Style)
            {
                case GraphEdgeStyle.Flow:
                    DrawFlow(context, edge.Points[0], edge.Points[^1]);
                    break;
                case GraphEdgeStyle.Relative:
                    DrawPolyline(context, RelativePen, edge.Points);
                    // A ring at the anchor ("positioned from here"), an arrow at the focus that follows it.
                    context.DrawEllipse(null, RelativeRingPen, edge.Points[0], 4, 4);
                    DrawArrowHead(context, RelativeRingPen, edge.Points[^2], edge.Points[^1]);
                    break;
                case GraphEdgeStyle.Preview:
                    DrawPolyline(context, PreviewPen, edge.Points);
                    context.DrawEllipse(PreviewPen.Brush, null, edge.Points[^1], 4, 4);
                    break;
                case GraphEdgeStyle.Exclusive:
                    DrawPolyline(context, ExclusivePen, edge.Points);
                    DrawExclusiveMark(context, edge.Points[0], edge.Points[^1]);
                    break;
                default:
                    DrawPolyline(context, edge.Style == GraphEdgeStyle.Dashed ? DashedPen : SolidPen, edge.Points);
                    break;
            }
        }
    }

    private static void DrawPolyline(DrawingContext context, IPen pen, IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], isFilled: false);
            for (var i = 1; i < points.Count; i++)
                g.LineTo(points[i]);
            g.EndFigure(isClosed: false);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawArrowHead(DrawingContext context, IPen pen, Point from, Point tip)
    {
        var direction = tip - from;
        var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (length < 1)
            return;
        var unit = direction / length;
        var normal = new Point(-unit.Y, unit.X);
        context.DrawLine(pen, tip, tip - unit * 7 + normal * 4);
        context.DrawLine(pen, tip, tip - unit * 7 - normal * 4);
    }

    /// <summary>The game marks mutually exclusive focuses with a symbol in the middle of the link.</summary>
    private static void DrawExclusiveMark(DrawingContext context, Point a, Point b)
    {
        var middle = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        context.DrawEllipse(new ImmutableSolidColorBrush(Color.Parse("#1E1F22")), ExclusivePen, middle, 7, 7);
        context.DrawLine(ExclusivePen, middle + new Point(-3.5, -3.5), middle + new Point(3.5, 3.5));
        context.DrawLine(ExclusivePen, middle + new Point(-3.5, 3.5), middle + new Point(3.5, -3.5));
    }

    /// <summary>A horizontal S-curve with an arrow head; loops back around when the target is to the left.</summary>
    private static void DrawFlow(DrawingContext context, Point start, Point end)
    {
        var reach = end.X > start.X ? Math.Max(40, (end.X - start.X) / 2) : 160;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(start, isFilled: false);
            g.CubicBezierTo(start + new Point(reach, 0), end - new Point(reach, 0), end - new Point(6, 0));
            g.EndFigure(isClosed: false);
        }
        context.DrawGeometry(null, FlowPen, geometry);

        var arrow = new StreamGeometry();
        using (var g = arrow.Open())
        {
            g.BeginFigure(end, isFilled: true);
            g.LineTo(end + new Point(-9, -5));
            g.LineTo(end + new Point(-9, 5));
            g.EndFigure(isClosed: true);
        }
        context.DrawGeometry(FlowBrush, null, arrow);
        context.DrawEllipse(FlowBrush, null, start, 3.5, 3.5);
    }
}
