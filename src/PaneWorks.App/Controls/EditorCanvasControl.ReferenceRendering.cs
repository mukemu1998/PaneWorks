using System.Windows.Media;
using PaneWorks.Core.Models;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App.Controls;

public sealed partial class EditorCanvasControl
{
    private void DrawReferenceLayouts(DrawingContext drawingContext)
    {
        var references = ReferenceLayouts?.ToList();
        if (references is null || references.Count == 0)
        {
            return;
        }

        var borderPen = new WpfPen(
            new SolidColorBrush(WpfColor.FromArgb(130, 255, 255, 255)),
            1.1);
        var splitterGlowPen = new WpfPen(
            new SolidColorBrush(WpfColor.FromArgb(42, 255, 255, 255)),
            5.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        var splitterPen = new WpfPen(
            new SolidColorBrush(WpfColor.FromArgb(230, 255, 255, 255)),
            2.1)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        foreach (var reference in references)
        {
            if (reference.StageBounds.Width <= 0 || reference.StageBounds.Height <= 0)
            {
                continue;
            }

            var geometry = _geometryCalculator.Compute(
                reference.Document,
                reference.StageBounds,
                VisibleSplitterThickness);

            drawingContext.DrawRectangle(null, borderPen, ToRect(reference.StageBounds));

            foreach (var splitter in MergeSplitterDrawSegments(geometry.Splitters, selectedNodeId: null))
            {
                if (splitter.Direction == SplitDirection.Vertical)
                {
                    var start = new WpfPoint(splitter.AxisPosition, splitter.Start);
                    var end = new WpfPoint(splitter.AxisPosition, splitter.End);
                    drawingContext.DrawLine(splitterGlowPen, start, end);
                    drawingContext.DrawLine(splitterPen, start, end);
                    continue;
                }

                var horizontalStart = new WpfPoint(splitter.Start, splitter.AxisPosition);
                var horizontalEnd = new WpfPoint(splitter.End, splitter.AxisPosition);
                drawingContext.DrawLine(splitterGlowPen, horizontalStart, horizontalEnd);
                drawingContext.DrawLine(splitterPen, horizontalStart, horizontalEnd);
            }
        }
    }
}
