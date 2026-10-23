using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using HoI4ModBuilder.ViewModels;

namespace HoI4ModBuilder.Views;

/// <summary>
/// Wrapping text whose link segments (files, ids) are clickable. Links are plain runs found by hit testing:
/// controls embedded in text (InlineUIContainer) crash Avalonia when it measures them during rendering.
/// </summary>
public class LinkedText : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<LogSegmentViewModel>?> SegmentsProperty =
        AvaloniaProperty.Register<LinkedText, IReadOnlyList<LogSegmentViewModel>?>(nameof(Segments));

    public static readonly StyledProperty<IBrush?> LinkBrushProperty =
        AvaloniaProperty.Register<LinkedText, IBrush?>(nameof(LinkBrush));

    private readonly List<(int Start, int End, LogSegmentViewModel Segment, Run Run)> _links = [];
    private Run? _hovered;

    public LinkedText() => TextWrapping = TextWrapping.Wrap;

    public IReadOnlyList<LogSegmentViewModel>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public IBrush? LinkBrush
    {
        get => GetValue(LinkBrushProperty);
        set => SetValue(LinkBrushProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SegmentsProperty || change.Property == LinkBrushProperty)
            Build();
    }

    private void Build()
    {
        _links.Clear();
        _hovered = null;
        var inlines = new InlineCollection();
        var position = 0;
        foreach (var segment in Segments ?? [])
        {
            var run = new Run(segment.Text);
            if (segment.IsLink)
            {
                if (LinkBrush is { } brush)
                    run.Foreground = brush;
                _links.Add((position, position + segment.Text.Length, segment, run));
            }
            inlines.Add(run);
            position += segment.Text.Length;
        }
        Inlines = inlines;
    }

    private (LogSegmentViewModel Segment, Run Run)? LinkAt(Point point)
    {
        if (_links.Count == 0)
            return null;
        var hit = TextLayout.HitTestPoint(point - new Point(Padding.Left, Padding.Top));
        if (!hit.IsInside)
            return null;
        var index = hit.TextPosition;
        foreach (var link in _links)
            if (index >= link.Start && index < link.End)
                return (link.Segment, link.Run);
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Hover(LinkAt(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Hover(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || LinkAt(e.GetPosition(this)) is not { } link)
            return;
        e.Handled = true;
        link.Segment.Open?.Execute(null);
    }

    private void Hover((LogSegmentViewModel Segment, Run Run)? link)
    {
        if (link?.Run == _hovered)
            return;
        if (_hovered is not null)
            _hovered.TextDecorations = null;
        _hovered = link?.Run;
        if (_hovered is not null)
            _hovered.TextDecorations = Avalonia.Media.TextDecorations.Underline;
        Cursor = link is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, link?.Segment.ToolTip);
    }
}
