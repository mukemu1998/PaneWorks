using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PaneWorks.App.Controls;

public sealed partial class EditorCanvasControl
{
    private void OpenContextMenu(string targetNodeId, bool includeSplitActions, bool includeDelete)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.MousePoint,
            StaysOpen = false,
            Focusable = true
        };

        if (includeSplitActions)
        {
            menu.Items.Add(CreateMenuItem("横向二等分", CanvasContextAction.SplitHorizontalHalf, targetNodeId));
            menu.Items.Add(CreateMenuItem("纵向二等分", CanvasContextAction.SplitVerticalHalf, targetNodeId));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem("横向三等分", CanvasContextAction.SplitHorizontalThirds, targetNodeId));
            menu.Items.Add(CreateMenuItem("纵向三等分", CanvasContextAction.SplitVerticalThirds, targetNodeId));
        }

        if (includeDelete)
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            menu.Items.Add(CreateMenuItem("删除当前分割", CanvasContextAction.Delete, targetNodeId));
        }

        AppendSnapLayoutMenu(menu);
        AppendWorkspaceProfileMenu(menu);

        if (menu.Items.Count == 0)
        {
            return;
        }

        menu.Opened += (_, _) => menu.Focus();
        menu.IsOpen = true;
    }

    private void AppendSnapLayoutMenu(ContextMenu menu)
    {
        var layouts = AvailableLayouts?.ToList();
        if (layouts is null || layouts.Count == 0)
        {
            return;
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var layoutMenu = new MenuItem
        {
            Header = "切换吸附布局"
        };

        foreach (var layout in layouts)
        {
            var layoutItem = new MenuItem
            {
                Header = layout.Name,
                IsCheckable = true,
                IsChecked = string.Equals(layout.Id, ActiveSnapLayoutId, StringComparison.OrdinalIgnoreCase),
                ToolTip = $"{layout.Id}.json"
            };

            layoutItem.Click += (_, _) =>
            {
                SnapLayoutSwitchRequested?.Invoke(this, new SnapLayoutSwitchRequestedEventArgs(layout.Id));
            };

            layoutMenu.Items.Add(layoutItem);
        }

        menu.Items.Add(layoutMenu);
    }

    private void AppendWorkspaceProfileMenu(ContextMenu menu)
    {
        var profiles = AvailableWorkspaceProfiles?.ToList();
        if (profiles is null || profiles.Count == 0)
        {
            return;
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var profileMenu = new MenuItem
        {
            Header = "切换并应用工作区方案"
        };

        foreach (var profile in profiles)
        {
            var profileItem = new MenuItem
            {
                Header = profile.Name,
                IsCheckable = true,
                IsChecked = string.Equals(profile.Id, ActiveWorkspaceProfileId, StringComparison.OrdinalIgnoreCase),
                ToolTip = profile.Description
            };

            profileItem.Click += (_, _) =>
            {
                WorkspaceProfileSwitchRequested?.Invoke(this, new WorkspaceProfileSwitchRequestedEventArgs(profile.Id));
            };

            profileMenu.Items.Add(profileItem);
        }

        menu.Items.Add(profileMenu);
    }

    private MenuItem CreateMenuItem(string header, CanvasContextAction action, string targetNodeId)
    {
        var item = new MenuItem
        {
            Header = header
        };

        item.Click += (_, _) =>
        {
            CanvasContextActionRequested?.Invoke(this, new CanvasContextActionRequestedEventArgs(action, targetNodeId));
        };

        return item;
    }
}
