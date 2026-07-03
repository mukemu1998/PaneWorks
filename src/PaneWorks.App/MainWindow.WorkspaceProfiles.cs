using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void ActivateSelectedWorkspaceProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedWorkspaceProfileItem is null)
        {
            WpfMessageBox.Show(
                this,
                "请先在工作区方案列表里选中一项，再启用。",
                "PaneWorks",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SwitchWorkspaceProfileAndResetRuntimeState(ViewModel.SelectedWorkspaceProfileItem.Id, notifyOnSuccess: true);
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
