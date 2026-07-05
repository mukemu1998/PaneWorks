using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaneWorks.Core.Models;
using WpfColor = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App.Controls;

public sealed partial class EditorCanvasControl
{
    private void DrawWorkspaceBindingMarkers(DrawingContext drawingContext)
    {
        if (!ShowWorkspaceBindingMarkers || _lastGeometry is null)
        {
            return;
        }

        var bindings = WorkspaceWindowBindings?.ToList();
        if (bindings is null || bindings.Count == 0)
        {
            return;
        }

        var currentDisplayId = BindingDisplayId ?? string.Empty;
        foreach (var region in _lastGeometry.Regions)
        {
            var regionBindings = bindings
                .Where(item =>
                    string.Equals(item.DisplayId, currentDisplayId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.NodeId, region.NodeId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.StackOrder)
                .ToList();
            if (regionBindings.Count == 0)
            {
                continue;
            }

            DrawBindingMarker(drawingContext, region.Bounds, regionBindings);
        }
    }

    private void DrawBindingMarker(DrawingContext drawingContext, PaneRect regionBounds, IReadOnlyList<WorkspaceWindowBinding> bindings)
    {
        const double defaultMarkerSize = 54;
        const double markerMargin = 10;
        var markerSize = Math.Min(
            defaultMarkerSize,
            Math.Max(36, Math.Min(regionBounds.Width, regionBounds.Height) - markerMargin));
        var markerX = regionBounds.X + Math.Min(markerMargin, Math.Max(0, (regionBounds.Width - markerSize) / 2));
        var markerY = regionBounds.Y + Math.Min(markerMargin, Math.Max(0, (regionBounds.Height - markerSize) / 2));
        var markerRect = new Rect(markerX, markerY, markerSize, markerSize);
        var background = new SolidColorBrush(WpfColor.FromArgb(220, 16, 22, 37));
        var stroke = new WpfPen(new SolidColorBrush(WpfColor.FromArgb(210, 159, 199, 255)), 1.2);
        drawingContext.DrawRoundedRectangle(background, stroke, markerRect, 16, 16);

        var visibleBindings = bindings
            .OrderBy(item => item.StackOrder)
            .TakeLast(3)
            .ToList();
        for (var index = 0; index < visibleBindings.Count; index++)
        {
            var itemBinding = visibleBindings[index];
            var iconRect = visibleBindings.Count == 1
                ? new Rect(markerRect.X + ((markerRect.Width - 32) / 2), markerRect.Y + 7, 32, 32)
                : new Rect(markerRect.X + 7 + (index * 7), markerRect.Y + 7 + (index * 4), 28, 28);
            var icon = TryGetIcon(itemBinding);
            if (icon is not null)
            {
                drawingContext.DrawImage(icon, iconRect);
            }
            else
            {
                var glyph = string.IsNullOrWhiteSpace(itemBinding.ProcessName)
                    ? "?"
                    : itemBinding.ProcessName.Trim()[0].ToString().ToUpperInvariant();
                var formatted = CreateBindingMarkerText(glyph, 18, WpfBrushes.White, FontWeights.Bold);
                drawingContext.DrawText(
                    formatted,
                    new WpfPoint(
                        iconRect.X + ((iconRect.Width - formatted.Width) / 2),
                        iconRect.Y + 3));
            }
        }

        if (bindings.Count > 1)
        {
            var countLabel = CreateBindingMarkerText($"+{bindings.Count - 1}", 10, WpfBrushes.White, FontWeights.Bold);
            drawingContext.DrawText(
                countLabel,
                new WpfPoint(
                    markerRect.Right - countLabel.Width - 6,
                    markerRect.Y + 5));
        }

        var binding = bindings.OrderBy(item => item.StackOrder).Last();
        var label = NormalizeProcessLabel(binding.ProcessName);
        if (!string.IsNullOrWhiteSpace(label))
        {
            var formattedLabel = CreateBindingMarkerText(label, 9.5, new SolidColorBrush(WpfColor.FromRgb(207, 231, 255)), FontWeights.SemiBold);
            drawingContext.DrawText(
                formattedLabel,
                new WpfPoint(
                    markerRect.X + ((markerRect.Width - formattedLabel.Width) / 2),
                    markerRect.Bottom - 14));
        }
    }

    private ImageSource? TryGetIcon(WorkspaceWindowBinding binding)
    {
        var path = binding.ExecutablePath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return null;
        }

        if (_iconCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                _iconCache[path] = null;
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            _iconCache[path] = source;
            return source;
        }
        catch
        {
            _iconCache[path] = null;
            return null;
        }
    }

    private static string NormalizeProcessLabel(string processName)
    {
        var label = processName.Trim();
        return label.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? label[..^4]
            : label;
    }

    private FormattedText CreateBindingMarkerText(string text, double size, WpfBrush brush, FontWeight weight)
    {
        return new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new WpfFontFamily("Microsoft YaHei UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}
