using System.Windows;
using System.Windows.Threading;
using PaneWorks.App.Diagnostics;
using WpfApplication = System.Windows.Application;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App;

public partial class MainWindow
{
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
