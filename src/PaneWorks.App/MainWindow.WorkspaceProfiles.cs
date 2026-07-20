using System.Windows;
using PaneWorks.App.ViewModels;
using PaneWorks.App.Views;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void ActivateSelectedWorkspaceProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsWorkspaceBindingMode)
        {
            PaneMessageService.Show(
                this,
                "请先退出“编辑绑定”，再切换并应用工作区方案。",
                buttons: MessageBoxButton.OK,
                image: MessageBoxImage.Information);
            return;
        }

        if (ViewModel.SelectedWorkspaceProfileItem is null)
        {
            PaneMessageService.Show(
                this,
                "请先在工作区方案列表里选中一项，再启用。",
                buttons: MessageBoxButton.OK,
                image: MessageBoxImage.Information);
            return;
        }

        if (ViewModel.SelectedWorkspaceProfileItem.HasWorkspaceWarning
            && !ConfirmApplyWorkspaceProfileWithWarning(ViewModel.SelectedWorkspaceProfileItem))
        {
            ViewModel.SetUserStatusMessage("已取消应用需检查的工作区方案。");
            return;
        }

        SwitchWorkspaceProfileAndResetRuntimeState(ViewModel.SelectedWorkspaceProfileItem.Id, notifyOnSuccess: true);
    }

    private bool ConfirmApplyWorkspaceProfileWithWarning(LayoutListItemViewModel profile)
    {
        var result = PaneMessageService.Show(
            this,
            $"工作区“{profile.Name}”存在风险，可能无法完整恢复：{Environment.NewLine}{profile.Description}{Environment.NewLine}{Environment.NewLine}仍然要继续应用这套工作区方案吗？",
            "工作区需检查",
            buttons: MessageBoxButton.YesNo,
            image: MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private void RefreshWorkspaceProfilesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsWorkspaceBindingMode)
        {
            return;
        }

        ViewModel.RefreshWorkspaceProfiles();
        ViewModel.SetUserStatusMessage("工作区方案状态已刷新。");
    }

    private IntPtr FindSnappedWindowHandleForRegion(string displayId, string nodeId)
    {
        var zOrderRanks = BuildDesktopZOrderRank();
        return _snapBindings
            .Where(binding => string.Equals(binding.Value.DisplayId, displayId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(binding.Value.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            .Select(binding => binding.Key)
            .OrderBy(handle => GetDesktopZOrderRank(handle, zOrderRanks))
            .FirstOrDefault();
    }
}
