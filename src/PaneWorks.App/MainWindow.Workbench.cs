using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;
using WpfApplication = System.Windows.Application;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void UpdateWorkbenchPanelPosition()
    {
        if (!IsLoaded)
        {
            return;
        }

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
        if (sender is UIElement { IsMouseCaptured: true } element)
        {
            element.ReleaseMouseCapture();
        }
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

    public void PrepareSecondaryDialog(Window dialog)
    {
        dialog.Owner = this;
        dialog.Topmost = Topmost;
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        PositionSecondaryDialog(dialog);
        dialog.SourceInitialized += (_, _) => PositionSecondaryDialog(dialog);
        dialog.Loaded += (_, _) => PositionSecondaryDialog(dialog);
        dialog.ContentRendered += (_, _) => PositionSecondaryDialog(dialog);
        dialog.Dispatcher.BeginInvoke(() => PositionSecondaryDialog(dialog), DispatcherPriority.ApplicationIdle);
    }

    private void PositionSecondaryDialog(Window dialog)
    {
        if (!IsLoaded || WorkbenchPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        WorkbenchPanel.UpdateLayout();
        dialog.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

        var panelTopLeft = DevicePointToDipPoint(WorkbenchPanel.PointToScreen(new WpfPoint(0, 0)));
        var panelWidth = WorkbenchPanel.ActualWidth > 0 ? WorkbenchPanel.ActualWidth : WorkbenchPanel.DesiredSize.Width;
        var panelHeight = WorkbenchPanel.ActualHeight > 0 ? WorkbenchPanel.ActualHeight : WorkbenchPanel.DesiredSize.Height;
        var dialogWidth = GetDialogDimension(dialog.ActualWidth, dialog.Width, dialog.DesiredSize.Width, 520);
        var dialogHeight = GetDialogDimension(dialog.ActualHeight, dialog.Height, dialog.DesiredSize.Height, 360);
        const double padding = 12;

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        var left = panelTopLeft.X + ((panelWidth - dialogWidth) / 2);
        var top = panelTopLeft.Y + ((panelHeight - dialogHeight) / 2);

        dialog.Left = Math.Clamp(left, virtualLeft + padding, Math.Max(virtualLeft + padding, virtualRight - dialogWidth - padding));
        dialog.Top = Math.Clamp(top, virtualTop + padding, Math.Max(virtualTop + padding, virtualBottom - dialogHeight - padding));
    }

    private static double GetDialogDimension(double actual, double requested, double desired, double fallback)
    {
        if (actual > 0)
        {
            return actual;
        }

        if (!double.IsNaN(requested) && requested > 0)
        {
            return requested;
        }

        return desired > 0 ? desired : fallback;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenSettings();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenAbout();
    }

    private void CloseWorkbenchButton_Click(object sender, RoutedEventArgs e)
    {
        MinimizeWorkbenchToTray();
    }

    private void MinimizeWorkbenchButton_Click(object sender, RoutedEventArgs e)
    {
        MinimizeWorkbenchToTaskbar();
    }

    private void SidebarWorkbenchButton_Click(object sender, RoutedEventArgs e)
    {
        MinimizeWorkbenchToSidebar();
    }

    private void RestoreWorkbenchButton_Click(object sender, RoutedEventArgs e)
    {
        RestoreWorkbenchFromSidebar();
    }

    private void MinimizeWorkbenchToSidebar()
    {
        EndWorkbenchPanelDrag(null);
        WorkbenchPanel.Visibility = Visibility.Collapsed;
        WorkbenchMiniBar.Visibility = Visibility.Visible;
        PaneWorksLog.Info("Workbench minimized to sidebar");
    }

    private void MinimizeWorkbenchToTaskbar()
    {
        EndWorkbenchPanelDrag(null);
        ShowInTaskbar = true;
        WindowState = WindowState.Minimized;
        PaneWorksLog.Info("Workbench minimized to taskbar");
    }

    private void MinimizeWorkbenchToTray()
    {
        EndWorkbenchPanelDrag(null);
        ((App)WpfApplication.Current).MinimizeMainWindowToTray();
    }

    private void RestoreWorkbenchFromSidebar()
    {
        WorkbenchMiniBar.Visibility = Visibility.Collapsed;
        WorkbenchPanel.Visibility = Visibility.Visible;
        PaneWorksLog.Info("Workbench restored from sidebar");
    }
}
