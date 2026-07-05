using System.Windows;
using System.Windows.Input;
using PaneWorks.Core.Models;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void WorkbenchMiniBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isWorkbenchMiniBarPointerDown = true;
        _isWorkbenchMiniBarDragging = false;
        _workbenchMiniBarDragStartPoint = e.GetPosition(this);
        _workbenchMiniBarDragStartOffsetX = WorkbenchMiniBarTranslate.X;
        _workbenchMiniBarDragStartOffsetY = WorkbenchMiniBarTranslate.Y;
        WorkbenchMiniBar.CaptureMouse();
        e.Handled = true;
    }

    private void WorkbenchMiniBar_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isWorkbenchMiniBarPointerDown)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndWorkbenchMiniBarDrag();
            return;
        }

        var currentPoint = e.GetPosition(this);
        var deltaX = currentPoint.X - _workbenchMiniBarDragStartPoint.X;
        var deltaY = currentPoint.Y - _workbenchMiniBarDragStartPoint.Y;
        if (!_isWorkbenchMiniBarDragging
            && Math.Abs(deltaX) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(deltaY) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isWorkbenchMiniBarDragging = true;
        SetWorkbenchMiniBarDragOffset(
            _workbenchMiniBarDragStartOffsetX + deltaX,
            _workbenchMiniBarDragStartOffsetY + deltaY);
        e.Handled = true;
    }

    private void WorkbenchMiniBar_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isWorkbenchMiniBarPointerDown)
        {
            return;
        }

        var wasDragging = _isWorkbenchMiniBarDragging;
        EndWorkbenchMiniBarDrag();
        e.Handled = true;

        if (wasDragging)
        {
            SnapWorkbenchMiniBarToNearestEdge();
        }
        else
        {
            RestoreWorkbenchFromSidebar();
        }
    }

    private void EndWorkbenchMiniBarDrag()
    {
        _isWorkbenchMiniBarPointerDown = false;
        _isWorkbenchMiniBarDragging = false;
        if (WorkbenchMiniBar.IsMouseCaptured)
        {
            WorkbenchMiniBar.ReleaseMouseCapture();
        }
    }

    private void SetWorkbenchMiniBarDragOffset(double offsetX, double offsetY)
    {
        var (minX, maxX, minY, maxY) = GetWorkbenchMiniBarDragRange();
        const double snapDistance = 72;
        var clampedX = Math.Clamp(offsetX, minX, maxX);
        var clampedY = Math.Clamp(offsetY, minY, maxY);

        if (Math.Abs(clampedX - minX) <= snapDistance)
        {
            clampedX = minX;
        }
        else if (Math.Abs(clampedX - maxX) <= snapDistance)
        {
            clampedX = maxX;
        }

        if (Math.Abs(clampedY - minY) <= snapDistance)
        {
            clampedY = minY;
        }
        else if (Math.Abs(clampedY - maxY) <= snapDistance)
        {
            clampedY = maxY;
        }

        WorkbenchMiniBarTranslate.X = clampedX;
        WorkbenchMiniBarTranslate.Y = clampedY;
    }

    private void SnapWorkbenchMiniBarToNearestEdge()
    {
        var (minX, maxX, minY, maxY) = GetWorkbenchMiniBarDragRange();
        var currentX = Math.Clamp(WorkbenchMiniBarTranslate.X, minX, maxX);
        var currentY = Math.Clamp(WorkbenchMiniBarTranslate.Y, minY, maxY);

        var leftDistance = Math.Abs(currentX - minX);
        var rightDistance = Math.Abs(currentX - maxX);
        var topDistance = Math.Abs(currentY - minY);
        var bottomDistance = Math.Abs(currentY - maxY);
        var nearestDistance = Math.Min(
            Math.Min(leftDistance, rightDistance),
            Math.Min(topDistance, bottomDistance));

        if (nearestDistance == leftDistance)
        {
            currentX = minX;
        }
        else if (nearestDistance == rightDistance)
        {
            currentX = maxX;
        }
        else if (nearestDistance == topDistance)
        {
            currentY = minY;
        }
        else
        {
            currentY = maxY;
        }

        WorkbenchMiniBarTranslate.X = currentX;
        WorkbenchMiniBarTranslate.Y = currentY;
    }

    private (double MinX, double MaxX, double MinY, double MaxY) GetWorkbenchMiniBarDragRange()
    {
        var barWidth = WorkbenchMiniBar.ActualWidth > 0
            ? WorkbenchMiniBar.ActualWidth
            : WorkbenchMiniBar.Width;
        var barHeight = WorkbenchMiniBar.ActualHeight > 0
            ? WorkbenchMiniBar.ActualHeight
            : WorkbenchMiniBar.Height;
        var hostBounds = GetWorkbenchMiniBarActiveScreenBounds();
        var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
        var windowHeight = ActualHeight > 0 ? ActualHeight : Height;
        const double padding = 8;

        var baseLeft = Math.Max(0, windowWidth - barWidth - WorkbenchMiniBar.Margin.Right);
        var baseTop = Math.Max(0, (windowHeight - barHeight + WorkbenchMiniBar.Margin.Top - WorkbenchMiniBar.Margin.Bottom) / 2);
        var minX = hostBounds.X + padding - baseLeft;
        var maxX = hostBounds.X + hostBounds.Width - barWidth - padding - baseLeft;
        var minY = hostBounds.Y + padding - baseTop;
        var maxY = hostBounds.Y + hostBounds.Height - barHeight - padding - baseTop;
        return (Math.Min(minX, maxX), Math.Max(minX, maxX), Math.Min(minY, maxY), Math.Max(minY, maxY));
    }

    private PaneRect GetWorkbenchMiniBarActiveScreenBounds()
    {
        if (TryGetCursorPosition(out var cursorInDevicePixels))
        {
            var display = _displayDiscoveryService.GetDisplayFromPoint((int)cursorInDevicePixels.X, (int)cursorInDevicePixels.Y);
            var displayDipBounds = DeviceRectToDipRect(display.Bounds);
            return new PaneRect(
                displayDipBounds.X - Left,
                displayDipBounds.Y - Top,
                displayDipBounds.Width,
                displayDipBounds.Height);
        }

        return new PaneRect(0, 0, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
    }
}
