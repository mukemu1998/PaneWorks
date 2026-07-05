using PaneWorks.App.Diagnostics;
using PaneWorks.App.ViewModels;

namespace PaneWorks.App;

public partial class MainWindow
{
    public IReadOnlyList<LayoutListItemViewModel> GetTrayLayoutItems()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(GetTrayLayoutItems);
        }

        return ViewModel.Layouts
            .Select(item => new LayoutListItemViewModel
            {
                Id = item.Id,
                Name = item.Name,
                Description = item.Description
            })
            .ToList();
    }

    public IReadOnlyList<LayoutListItemViewModel> GetTrayWorkspaceProfileItems()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(GetTrayWorkspaceProfileItems);
        }

        return ViewModel.WorkspaceProfiles
            .Select(item => new LayoutListItemViewModel
            {
                Id = item.Id,
                Name = item.Name,
                Description = item.Description
            })
            .ToList();
    }

    public string GetActiveSnapLayoutId()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(GetActiveSnapLayoutId);
        }

        return ViewModel.ActiveSnapLayoutId;
    }

    public string GetActiveWorkspaceProfileId()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(GetActiveWorkspaceProfileId);
        }

        return ViewModel.ActiveWorkspaceProfileId;
    }

    public void SwitchSnapLayoutFromTray(string layoutId)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SwitchSnapLayoutFromTray(layoutId));
            return;
        }

        SwitchSnapLayoutAndResetRuntimeState(layoutId);
    }

    public void SwitchWorkspaceProfileFromTray(string profileId)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SwitchWorkspaceProfileFromTray(profileId));
            return;
        }

        SwitchWorkspaceProfileAndResetRuntimeState(profileId, notifyOnSuccess: false);
    }

    public void PrepareForTrayRestore()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(PrepareForTrayRestore);
            return;
        }

        PaneWorksLog.Info("Prepare main window restore from tray");
        EnsureSnapOverlayHidden();
        RestoreWorkbenchFromSidebar();
    }
}
