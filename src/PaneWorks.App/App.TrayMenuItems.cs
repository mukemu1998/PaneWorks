using WpfControls = System.Windows.Controls;

namespace PaneWorks.App;

public partial class App
{
    private WpfControls.MenuItem CreateWpfLayoutsMenuItem()
    {
        var item = CreateWpfMenuItem("切换吸附布局", null);
        if (_mainWindow is null)
        {
            item.IsEnabled = false;
            return item;
        }

        var layouts = _cachedTrayLayouts;
        var activeLayoutId = _cachedActiveSnapLayoutId;

        if (layouts.Count == 0)
        {
            var emptyItem = CreateWpfMenuItem("暂无已保存分区布局", null);
            emptyItem.IsEnabled = false;
            item.Items.Add(emptyItem);
            return item;
        }

        foreach (var layout in layouts)
        {
            var layoutId = layout.Id;
            var layoutItem = CreateWpfMenuItem(layout.Name, () => SwitchSnapLayoutFromTray(layoutId));
            layoutItem.IsCheckable = true;
            layoutItem.IsChecked = string.Equals(layout.Id, activeLayoutId, StringComparison.OrdinalIgnoreCase);
            layoutItem.ToolTip = layout.Description;
            item.Items.Add(layoutItem);
        }

        return item;
    }

    private WpfControls.MenuItem CreateWpfWorkspaceProfilesMenuItem()
    {
        var item = CreateWpfMenuItem("切换并应用工作区方案", null);
        if (_mainWindow is null)
        {
            item.IsEnabled = false;
            return item;
        }

        var profiles = _cachedTrayWorkspaceProfiles;
        var activeProfileId = _cachedActiveWorkspaceProfileId;

        if (profiles.Count == 0)
        {
            var emptyItem = CreateWpfMenuItem("暂无已保存工作区方案", null);
            emptyItem.IsEnabled = false;
            item.Items.Add(emptyItem);
            return item;
        }

        foreach (var profile in profiles)
        {
            var profileId = profile.Id;
            var profileItem = CreateWpfMenuItem(profile.Name, () => SwitchWorkspaceProfileFromTray(profileId));
            profileItem.IsCheckable = true;
            profileItem.IsChecked = string.Equals(profile.Id, activeProfileId, StringComparison.OrdinalIgnoreCase);
            profileItem.ToolTip = profile.Description;
            item.Items.Add(profileItem);
        }

        return item;
    }
}
