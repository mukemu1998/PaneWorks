using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private static WorkspaceProfileDocument ReplaceWindowBindingsForRegion(
        WorkspaceProfileDocument profile,
        WorkspaceWindowBinding binding)
    {
        var bindings = RemoveWindowBinding(profile, binding.DisplayId, binding.NodeId).WindowBindings
            ?? new List<WorkspaceWindowBinding>();
        bindings.Add(binding with { StackOrder = 0 });
        return profile with { WindowBindings = bindings };
    }

    private static WorkspaceProfileDocument ReplaceWindowBindingGroups(
        WorkspaceProfileDocument profile,
        IReadOnlyList<WorkspaceWindowBinding> incomingBindings)
    {
        var bindings = NormalizeWindowBindings(profile.WindowBindings);
        var affectedRegionKeys = incomingBindings
            .Select(item => $"{item.DisplayId}::{item.NodeId}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bindings = bindings
            .Where(item => !affectedRegionKeys.Contains($"{item.DisplayId}::{item.NodeId}"))
            .ToList();

        foreach (var group in incomingBindings.GroupBy(item => $"{item.DisplayId}::{item.NodeId}", StringComparer.OrdinalIgnoreCase))
        {
            var stackOrder = 0;
            foreach (var binding in group.OrderBy(item => item.StackOrder))
            {
                bindings.Add(binding with { StackOrder = stackOrder });
                stackOrder++;
            }
        }

        return profile with { WindowBindings = bindings };
    }

    private static WorkspaceProfileDocument UpsertWindowBindingPatch(WorkspaceProfileDocument profile, WorkspaceWindowBinding binding)
    {
        var bindings = NormalizeWindowBindings(profile.WindowBindings);
        var index = bindings.FindIndex(item => IsSameWindowBindingSlot(item, binding));

        if (index >= 0)
        {
            bindings[index] = binding;
        }
        else
        {
            bindings.Add(binding);
        }

        return profile with { WindowBindings = bindings };
    }

    private static WorkspaceProfileDocument RemoveWindowBinding(WorkspaceProfileDocument profile, string displayId, string nodeId)
    {
        var bindings = NormalizeWindowBindings(profile.WindowBindings)
            .Where(item =>
                !string.Equals(item.DisplayId, displayId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(item.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return profile with { WindowBindings = bindings };
    }
}
