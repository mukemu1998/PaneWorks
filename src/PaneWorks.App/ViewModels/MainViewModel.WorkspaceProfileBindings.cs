using System;
using System.Collections.Generic;
using System.Linq;
using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private static WorkspaceProfileDocument NormalizeWorkspaceProfile(WorkspaceProfileDocument profile)
    {
        return profile with
        {
            Version = Math.Max(2, profile.Version),
            Name = NormalizeLayoutName(profile.Name),
            LayoutId = profile.LayoutId?.Trim() ?? string.Empty,
            WindowBindings = NormalizeWindowBindings(profile.WindowBindings)
        };
    }

    private static WorkspaceWindowBinding? GetWindowBinding(WorkspaceProfileDocument profile, string displayId, string nodeId)
    {
        return GetWindowBindings(profile, displayId, nodeId)
            .OrderBy(item => item.StackOrder)
            .LastOrDefault();
    }

    private static List<WorkspaceWindowBinding> GetWindowBindings(WorkspaceProfileDocument profile, string displayId, string nodeId)
    {
        return NormalizeWindowBindings(profile.WindowBindings)
            .Where(item =>
                string.Equals(item.DisplayId, displayId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.StackOrder)
            .ToList();
    }

    private static bool IsSameWindowBindingSlot(WorkspaceWindowBinding left, WorkspaceWindowBinding right)
    {
        return string.Equals(left.DisplayId, right.DisplayId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.NodeId, right.NodeId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.WindowTitleSnapshot, right.WindowTitleSnapshot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            && left.StackOrder == right.StackOrder;
    }

}
