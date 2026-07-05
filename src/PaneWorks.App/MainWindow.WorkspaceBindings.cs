using System.Windows;
using System.Windows.Interop;
using PaneWorks.App.Diagnostics;
using PaneWorks.App.Views;
using WpfMessageBox = System.Windows.MessageBox;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void BindWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditWorkspaceBindings)
        {
            WpfMessageBox.Show(
                this,
                "请先选中工作区方案并点击“编辑绑定”，再给区域绑定窗口。",
                "PaneWorks",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ViewModel.TryGetSelectedLeafRegion(out var displayId, out var nodeId))
        {
            WpfMessageBox.Show(
                this,
                "请先点击一个区域，再给它绑定窗口。",
                "PaneWorks",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var windows = _workspaceApplyService
            .GetVisibleWindows(new WindowInteropHelper(this).Handle)
            .OrderBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (windows.Count == 0)
        {
            WpfMessageBox.Show(
                this,
                "当前没有可绑定的桌面窗口。请先打开几个普通应用窗口再试。",
                "PaneWorks",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new WindowBindingPickerDialog(
            windows,
            $"当前区域：{nodeId}  |  当前屏幕：{ViewModel.CurrentDisplayName}");
        dialog.PreferredWindowHandle = FindSnappedWindowHandleForRegion(displayId, nodeId);
        PrepareSecondaryDialog(dialog);

        if (dialog.ShowDialog() != true || dialog.SelectedWindow is null)
        {
            return;
        }

        if (!ViewModel.TrySetSelectedRegionWindowBinding(
                dialog.SelectedWindow.ProcessName,
                dialog.SelectedWindow.Title,
                dialog.SelectedWindow.ExecutablePath,
                dialog.SelectedWindow.ExplorerFolderPath,
                out var message))
        {
            WpfMessageBox.Show(
                this,
                message,
                "PaneWorks",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        WpfMessageBox.Show(
            this,
            $"{message} 点击“应用选中工作区”即可测试重新吸附，切换工作区时也会自动应用。",
            "PaneWorks",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void AutoBindSnappedWindowsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditWorkspaceBindings)
        {
            ViewModel.SetUserStatusMessage("请先选中工作区方案并点击“编辑绑定”，再一键绑定已吸附窗口。");
            return;
        }

        if (!TryCaptureSnappedWorkspaceWindowBindingRequests(out var requests, out var buildMessage))
        {
            ViewModel.SetUserStatusMessage(buildMessage);
            return;
        }

        var button = sender as System.Windows.Controls.Button;
        if (button is not null)
        {
            button.IsEnabled = false;
        }

        try
        {
            PaneWorksLog.Info($"Auto workspace bind started: requests={requests.Count}");
            var excludedWindowHandle = new WindowInteropHelper(this).Handle;
            var bindings = await Task.Run(() => BuildSnappedWorkspaceWindowBindings(requests, excludedWindowHandle));
            PaneWorksLog.Info($"Auto workspace bind built: bindings={bindings.Count}");

            if (bindings.Count == 0)
            {
                ViewModel.SetUserStatusMessage("没有找到可写入工作区的已吸附窗口。");
                return;
            }

            if (!ViewModel.TryUpsertWorkspaceWindowBindingsFast(bindings, out var message))
            {
                ViewModel.SetUserStatusMessage(message);
                return;
            }

            PaneWorksLog.Info($"Auto workspace bind saved: bindings={bindings.Count}");
            QueueExplorerFolderBindingCompletion(requests);
        }
        catch (Exception exception)
        {
            PaneWorksLog.Error("Auto workspace bind failed", exception);
            ViewModel.SetUserStatusMessage($"一键绑定失败：{exception.Message}");
        }
        finally
        {
            if (button is not null)
            {
                button.IsEnabled = true;
            }
        }
    }

    private void ClearWindowBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryClearSelectedRegionWindowBinding(out var message))
        {
            ViewModel.SetUserStatusMessage(message);
            return;
        }

        ViewModel.SetUserStatusMessage(message);
    }

    private void ClearAllWindowBindingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryClearAllWorkspaceWindowBindingsFast(out var message))
        {
            ViewModel.SetUserStatusMessage(message);
            return;
        }

        ViewModel.SetUserStatusMessage(message);
    }

}
