using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PaneWorks.Core.Models;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void UpdateWorkbenchPanelPosition()
    {
        var primaryDisplay = _displayDiscoveryService.GetPrimaryDisplay();
        var primaryDisplayDipBounds = DeviceRectToDipRect(primaryDisplay.Bounds);
        WorkbenchPanel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

        var panelWidth = WorkbenchPanel.Width > 0
            ? WorkbenchPanel.Width
            : WorkbenchPanel.DesiredSize.Width;
        var panelHeight = WorkbenchPanel.ActualHeight > 0
            ? WorkbenchPanel.ActualHeight
            : WorkbenchPanel.DesiredSize.Height;

        var preferredLeft = (primaryDisplayDipBounds.X - _virtualDesktopBounds.X)
            + Math.Max(0, (primaryDisplayDipBounds.Width - panelWidth) / 2);
        var leftMin = primaryDisplayDipBounds.X - _virtualDesktopBounds.X + 32;
        var leftMax = primaryDisplayDipBounds.X - _virtualDesktopBounds.X + Math.Max(32, primaryDisplayDipBounds.Width - panelWidth - 32);
        var left = Math.Clamp(preferredLeft, leftMin, leftMax);
        var top = (primaryDisplayDipBounds.Y - _virtualDesktopBounds.Y)
            + Math.Max(24, (primaryDisplayDipBounds.Height - panelHeight) / 2);

        WorkbenchPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        WorkbenchPanel.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        WorkbenchPanel.Margin = new Thickness(left, top, 0, 0);
    }

    private void WorkbenchDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isWorkbenchPanelDragging = true;
        _workbenchPanelDragStartPoint = e.GetPosition(this);
        _workbenchPanelDragStartOffsetX = WorkbenchPanelTranslate.X;
        _workbenchPanelDragStartOffsetY = WorkbenchPanelTranslate.Y;
        BeginWorkbenchPanelDragVisualOptimization();

        if (sender is UIElement element)
        {
            element.CaptureMouse();
        }

        e.Handled = true;
    }

    private void WorkbenchDragHandle_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isWorkbenchPanelDragging)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndWorkbenchPanelDrag(sender);
            return;
        }

        var currentPoint = e.GetPosition(this);
        SetWorkbenchPanelDragOffset(
            _workbenchPanelDragStartOffsetX + currentPoint.X - _workbenchPanelDragStartPoint.X,
            _workbenchPanelDragStartOffsetY + currentPoint.Y - _workbenchPanelDragStartPoint.Y);
        e.Handled = true;
    }

    private void WorkbenchDragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndWorkbenchPanelDrag(sender);
        e.Handled = true;
    }

    private void EndWorkbenchPanelDrag(object? sender)
    {
        if (!_isWorkbenchPanelDragging)
        {
            return;
        }

        _isWorkbenchPanelDragging = false;
        EndWorkbenchPanelDragVisualOptimization();
        if (sender is UIElement { IsMouseCaptured: true } element)
        {
            element.ReleaseMouseCapture();
        }
    }

    private void BeginWorkbenchPanelDragVisualOptimization()
    {
        _workbenchPanelOriginalCacheMode ??= WorkbenchPanel.CacheMode;
        WorkbenchPanel.CacheMode = new BitmapCache
        {
            EnableClearType = false,
            RenderAtScale = 1
        };
    }

    private void EndWorkbenchPanelDragVisualOptimization()
    {
        WorkbenchPanel.CacheMode = _workbenchPanelOriginalCacheMode;
    }

    private void SetWorkbenchPanelDragOffset(double offsetX, double offsetY)
    {
        var panelWidth = WorkbenchPanel.ActualWidth > 0
            ? WorkbenchPanel.ActualWidth
            : WorkbenchPanel.DesiredSize.Width;
        var panelHeight = WorkbenchPanel.ActualHeight > 0
            ? WorkbenchPanel.ActualHeight
            : WorkbenchPanel.DesiredSize.Height;

        if (ActualWidth > 0 && panelWidth > 0)
        {
            var minOffsetX = 12 - WorkbenchPanel.Margin.Left;
            var maxOffsetX = Math.Max(
                minOffsetX,
                ActualWidth - WorkbenchPanel.Margin.Left - panelWidth - 12);
            offsetX = Math.Clamp(offsetX, minOffsetX, maxOffsetX);
        }

        if (ActualHeight > 0 && panelHeight > 0)
        {
            var minOffsetY = 12 - WorkbenchPanel.Margin.Top;
            var maxOffsetY = Math.Max(
                minOffsetY,
                ActualHeight - WorkbenchPanel.Margin.Top - panelHeight - 12);
            offsetY = Math.Clamp(offsetY, minOffsetY, maxOffsetY);
        }

        WorkbenchPanelTranslate.X = offsetX;
        WorkbenchPanelTranslate.Y = offsetY;
    }

}
