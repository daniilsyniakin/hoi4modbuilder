using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using HoI4ModBuilder.Clausewitz;

namespace HoI4ModBuilder.Editor;

/// <summary>Draws wavy underlines under the analyzer's diagnostics.</summary>
public sealed class DiagnosticRenderer : IBackgroundRenderer
{
    private const double WaveStep = 2;
    private const double WaveHeight = 2;
    private const double MinWidth = 6;

    private static readonly IPen ErrorPen = new ImmutablePen(EditorColors.Error.ToUInt32(), 1);
    private static readonly IPen WarningPen = new ImmutablePen(EditorColors.Warning.ToUInt32(), 1);

    public ScriptAnalyzer? Analyzer { get; set; }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Analyzer is null || !textView.VisualLinesValid || textView.VisualLines.Count == 0)
            return;

        var viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
        var viewEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;

        foreach (var marker in Analyzer.Markers.FindOverlappingSegments(viewStart, viewEnd - viewStart))
        {
            var pen = marker.Diagnostic.Severity == DiagnosticSeverity.Error ? ErrorPen : WarningPen;
            // A marker collapsed by edits still deserves a visible hint.
            ISegment segment = marker.Length > 0
                ? marker
                : new TextSegment { StartOffset = marker.StartOffset, Length = Math.Min(1, textView.Document.TextLength - marker.StartOffset) };

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                DrawWave(drawingContext, pen, rect.Left, rect.Bottom - 1, Math.Max(rect.Width, MinWidth));
        }
    }

    private static void DrawWave(DrawingContext context, IPen pen, double left, double baseline, double width)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(left, baseline), false);
            var up = true;
            for (var x = left + WaveStep; x <= left + width; x += WaveStep)
            {
                g.LineTo(new Point(x, up ? baseline - WaveHeight : baseline));
                up = !up;
            }
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}
