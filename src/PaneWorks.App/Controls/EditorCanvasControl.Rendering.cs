using System;
using System.Collections.Generic;
using System.Globalization;
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

        if (PreviewBounds is { } previewBounds)
        {
            var previewRect = ToRect(previewBounds);
            drawingContext.DrawRoundedRectangle(
                new SolidColorBrush(WpfColor.FromArgb(86, 41, 216, 242)),
                new WpfPen(new SolidColorBrush(WpfColor.FromRgb(255, 99, 245)), 3.4),
                previewRect,
                10,
                10);
        }

        var stageRect = ToRect(stageBounds);
        if (IsLayoutEditingEnabled)
        {
            var stageGlowRect = InsetRect(stageRect, 7);
            var stageBorderRect = InsetRect(stageRect, 3);
            drawingContext.DrawRectangle(
                null,
                new WpfPen(new SolidColorBrush(WpfColor.FromArgb(185, 255, 44, 52)), 13),
                stageGlowRect);
            drawingContext.DrawRectangle(
                null,
                new WpfPen(new SolidColorBrush(WpfColor.FromRgb(255, 38, 47)), 5),
                stageBorderRect);
            DrawEditingDisplayLabel(drawingContext, stageRect);
        }
        else
        {
            drawingContext.DrawRectangle(
                null,
                new WpfPen(new SolidColorBrush(WpfColor.FromArgb(210, 255, 255, 255)), 1),
                stageRect);
        }

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

    private static Rect InsetRect(Rect rect, double inset)
    {
        return new Rect(
            rect.X + inset,
            rect.Y + inset,
            Math.Max(0, rect.Width - (inset * 2)),
            Math.Max(0, rect.Height - (inset * 2)));
    }

    private void DrawEditingDisplayLabel(DrawingContext drawingContext, Rect stageRect)
    {
        if (string.IsNullOrWhiteSpace(EditingDisplayName))
        {
            return;
        }

        var text = new FormattedText(
            $"正在编辑：{EditingDisplayName}",
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            16,
            new SolidColorBrush(WpfColor.FromRgb(255, 38, 47)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var labelRect = new Rect(
            stageRect.X + Math.Max(10, (stageRect.Width - text.Width - 18) / 2),
            stageRect.Y + 14,
            text.Width + 18,
            text.Height + 8);
        drawingContext.DrawRoundedRectangle(
            new SolidColorBrush(WpfColor.FromArgb(212, 70, 14, 18)),
            new WpfPen(new SolidColorBrush(WpfColor.FromArgb(220, 255, 38, 47)), 1),
            labelRect,
            8,
            8);
        drawingContext.DrawText(text, new WpfPoint(labelRect.X + 9, labelRect.Y + 4));
    }
}
