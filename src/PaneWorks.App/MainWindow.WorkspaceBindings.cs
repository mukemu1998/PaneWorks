using System.Windows;
using System.Windows.Interop;
using PaneWorks.App.Diagnostics;
using PaneWorks.App.Views;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private async void BindWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditWorkspaceBindings)
        {
            PaneMessageService.Show(
                this,
                "请先选中工作区方案并点击“编辑绑定”，再给区域绑定窗口。",
                buttons: MessageBoxButton.OK,
                image: MessageBoxImage.Information);
            return;
        }

        if (!ViewModel.TryGetSelectedLeafRegion(out var displayId, out var nodeId))
        {
            PaneMessageService.Show(
                this,
                "请先点击一个区域，再给它绑定窗口。",
                buttons: MessageBoxButton.OK,
                image: MessageBoxImage.Information);
            return;
        }

        try
        {
            if (WindowBindingPickerOverlay.Visibility == Visibility.Visible)
            {
                return;
            }

            ViewModel.SetUserStatusMessage("正在读取可绑定的桌面窗口...");
            var mainWindowHandle = new WindowInteropHelper(this).Handle;
            PaneWorksLog.Info("Manual workspace bind: start collecting visible windows");
            var windows = await Task.Run(() => _workspaceApplyService
                .GetVisibleWindows(mainWindowHandle)
                .OrderBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ToList());
            PaneWorksLog.Info($"Manual workspace bind: collected {windows.Count} visible windows");

            if (windows.Count == 0)
            {
                PaneMessageService.Show(
                    this,
                    "当前没有可绑定的桌面窗口。请先打开几个普通应用窗口再试。",
                    buttons: MessageBoxButton.OK,
                    image: MessageBoxImage.Information);
                return;
            }

            _pendingWindowBindingDisplayId = displayId;
            _pendingWindowBindingNodeId = nodeId;
            WindowBindingPickerOverlay.Open(
                windows,
                $"当前区域：{nodeId}  |  当前屏幕：{ViewModel.CurrentDisplayName}",
                FindSnappedWindowHandleForRegion(displayId, nodeId));
            PaneWorksLog.Info("Manual workspace bind: picker overlay opened");
        }
        catch (Exception ex)
        {
            PaneWorksLog.Error("Manual workspace bind failed", ex);
            PaneMessageService.Show(
                this,
                $"读取可绑定窗口失败：{ex.Message}",
                buttons: MessageBoxButton.OK,
                image: MessageBoxImage.Error);
        }
    }

    private void WindowBindingPickerOverlay_BindingConfirmed(object? sender, EventArgs e)
    {
        if (WindowBindingPickerOverlay.SelectedWindow is not { } selectedWindow
            || string.IsNullOrWhiteSpace(_pendingWindowBindingDisplayId)
            || string.IsNullOrWhiteSpace(_pendingWindowBindingNodeId))
        {
            return;
        }

        if (!string.Equals(ViewModel.SelectedDisplayItem?.Id, _pendingWindowBindingDisplayId, StringComparison.OrdinalIgnoreCase))
        {
            ViewModel.SelectDisplayForLayoutEditing(_pendingWindowBindingDisplayId);
        }

        ViewModel.SelectNode(_pendingWindowBindingNodeId);
        ApplySelectedWindowBinding(selectedWindow);
        _pendingWindowBindingDisplayId = null;
        _pendingWindowBindingNodeId = null;
    }

    private void ApplySelectedWindowBinding(VisibleWindowInfo selectedWindow)
    {
        PaneWorksLog.Info($"Manual workspace bind: applying {selectedWindow.ProcessName}.exe");
        if (!ViewModel.TrySetSelectedRegionWindowBinding(
                selectedWindow.ProcessName,
                selectedWindow.Title,
                selectedWindow.ExecutablePath,
                selectedWindow.ExplorerFolderPath,
                out var message))
        {
            ViewModel.SetUserStatusMessage(message);
            return;
        }

        PaneWorksLog.Info("Manual workspace bind: queued background save");
        ViewModel.SetUserStatusMessage($"{message} 可直接继续绑定其它区域，保存完成后会自动更新方案。 ");
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
