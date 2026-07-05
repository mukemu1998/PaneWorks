using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using PaneWorks.Core.Models;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App.Controls;

public sealed partial class EditorCanvasControl
{
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (Document is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        DrawReferenceLayouts(drawingContext);

        var stageBounds = GetDesktopStageBounds();
        _lastGeometry = _geometryCalculator.Compute(Document, stageBounds, VisibleSplitterThickness);

        foreach (var region in _lastGeometry.Regions)
        {
            var rect = ToRect(region.Bounds);
            var isSelected = region.NodeId == SelectedNodeId;
            var isPreview = region.NodeId == PreviewNodeId;

            if (isPreview)
            {
                drawingContext.DrawRectangle(new SolidColorBrush(WpfColor.FromArgb(52, 74, 222, 128)), null, rect);
            }
            else if (isSelected)
            {
                drawingContext.DrawRectangle(new SolidColorBrush(WpfColor.FromArgb(34, 84, 166, 255)), null, rect);
            }

            if (isPreview || isSelected)
            {
                drawingContext.DrawRectangle(
                    null,
                    new WpfPen(
                        new SolidColorBrush(isPreview ? WpfColor.FromRgb(74, 222, 128) : WpfColor.FromRgb(84, 166, 255)),
                        isPreview ? 3.2 : 3),
                    rect);
            }
        }

        drawingContext.DrawRectangle(
            null,
            new WpfPen(new SolidColorBrush(WpfColor.FromArgb(210, 255, 255, 255)), 1),
            ToRect(stageBounds));

        foreach (var splitter in MergeSplitterDrawSegments(_lastGeometry.Splitters, SelectedNodeId))
        {
            var basePen = new WpfPen(
                new SolidColorBrush(splitter.IsSelected ? WpfColor.FromRgb(84, 166, 255) : WpfColor.FromRgb(255, 214, 64)),
                splitter.IsSelected ? 3.2 : 2.2)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            var glowPen = new WpfPen(
                new SolidColorBrush(splitter.IsSelected ? WpfColor.FromArgb(92, 84, 166, 255) : WpfColor.FromArgb(48, 255, 214, 64)),
                splitter.IsSelected ? 7 : 4.5)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            if (splitter.Direction == SplitDirection.Vertical)
            {
                drawingContext.DrawLine(
                    glowPen,
                    new WpfPoint(splitter.AxisPosition, splitter.Start),
                    new WpfPoint(splitter.AxisPosition, splitter.End));
                drawingContext.DrawLine(
                    basePen,
                    new WpfPoint(splitter.AxisPosition, splitter.Start),
                    new WpfPoint(splitter.AxisPosition, splitter.End));
                continue;
            }

            drawingContext.DrawLine(
                glowPen,
                new WpfPoint(splitter.Start, splitter.AxisPosition),
                new WpfPoint(splitter.End, splitter.AxisPosition));
            drawingContext.DrawLine(
                basePen,
                new WpfPoint(splitter.Start, splitter.AxisPosition),
                new WpfPoint(splitter.End, splitter.AxisPosition));
        }

        DrawWorkspaceBindingMarkers(drawingContext);

        if (_snapGuideAxisPosition.HasValue && _snapGuideDirection.HasValue)
        {
            var guidePen = new WpfPen(
                new SolidColorBrush(WpfColor.FromArgb(180, 255, 248, 182)),
                1.4)
            {
                DashStyle = DashStyles.Dash,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            if (_snapGuideDirection == SplitDirection.Vertical)
            {
                drawingContext.DrawLine(
                    guidePen,
                    new WpfPoint(_snapGuideAxisPosition.Value, stageBounds.Y),
                    new WpfPoint(_snapGuideAxisPosition.Value, stageBounds.Y + stageBounds.Height));
            }
            else
            {
                drawingContext.DrawLine(
                    guidePen,
                    new WpfPoint(stageBounds.X, _snapGuideAxisPosition.Value),
                    new WpfPoint(stageBounds.X + stageBounds.Width, _snapGuideAxisPosition.Value));
            }
        }
    }

    private static Rect ToRect(PaneRect rect)
    {
        return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }
}
