using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private async Task<int> RestoreLaunchedWorkspaceWindowsAsync(
        IReadOnlyList<WorkspaceWindowBinding> bindings,
        IntPtr excludedWindowHandle)
    {
        var watchBindings = bindings
            .Where(CanLaunchWorkspaceBinding)
            .ToList();
        var restoredBindingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deadline = DateTimeOffset.UtcNow + WorkspaceLaunchRestoreTimeout;
        var attempt = 0;
        var includeExplorerFolderPath = watchBindings.Any(IsExplorerFolderBinding);

        while (watchBindings.Count > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(attempt == 0
                ? WorkspaceLaunchInitialRestoreDelay
                : WorkspaceLaunchRestoreRetryInterval);
            attempt++;

            var bindingsToMatch = watchBindings
                .Where(binding => !TryGetLiveRuntimeBindingHandle(binding, out _))
                .ToList();
            if (bindingsToMatch.Count == 0)
            {
                ScheduleLaunchedWorkspaceWindowHandoffWatch(
                    watchBindings,
                    excludedWindowHandle,
                    includeExplorerFolderPath,
                    deadline);
                break;
            }

            var snappedHandles = _snapBindings.Keys.ToHashSet();
            var visibleWindows = _workspaceApplyService
                .GetVisibleWindows(excludedWindowHandle, includeExplorerFolderPath)
                .Where(window => !snappedHandles.Contains(window.Handle))
                .ToList();
            var matches = MatchWindowBindings(bindingsToMatch, visibleWindows);
            if (matches.Count == 0)
            {
                continue;
            }

            _ = ApplyWorkspaceWindowMatches(matches);
            RestoreRuntimeWorkspaceStackOrder(watchBindings);
            foreach (var match in matches)
            {
                if (TryGetLiveRuntimeBindingHandle(match.Binding, out _))
                {
                    restoredBindingKeys.Add(GetBindingInstanceKey(match.Binding));
                }
            }
            ScheduleWorkspaceStackOrderStabilization(watchBindings, "workspace-launch-restore");

            if (watchBindings.All(binding => TryGetLiveRuntimeBindingHandle(binding, out _)))
            {
                ScheduleLaunchedWorkspaceWindowHandoffWatch(
                    watchBindings,
                    excludedWindowHandle,
                    includeExplorerFolderPath,
                    deadline);
                break;
            }
        }

        var liveCount = watchBindings.Count(binding => TryGetLiveRuntimeBindingHandle(binding, out _));
        PaneWorksLog.Info($"Launched workspace restore finished: attempts={attempt}, restored={restoredBindingKeys.Count}, live={liveCount}, watched={watchBindings.Count}");
        return restoredBindingKeys.Count;
    }

    private int ApplyWorkspaceWindowMatches(IReadOnlyList<MatchedWindowBinding> matches)
    {
        if (matches.Count == 0)
        {
            return 0;
        }

        var restoredCount = 0;
        var restoredMatches = new List<MatchedWindowBinding>();
        var geometryByDisplay = new Dictionary<string, IReadOnlyDictionary<string, ComputedRegion>>(StringComparer.OrdinalIgnoreCase);

        BeginInternalWindowLayoutUpdate();
        try
        {
            foreach (var match in matches)
            {
                if (!TryGetBindingRegion(match.Binding, geometryByDisplay, out var region))
                {
                    continue;
                }

                var restoreBounds = ResolveRestoreBoundsForSnap(match.Window.Handle);
                if (TrySnapWindowToBoundsWithStatus(match.Window.Handle, region.Bounds, "workspace-launch-restore"))
                {
                    _snapBindings[match.Window.Handle] = new SnapBindingState(
                        match.Binding.NodeId,
                        match.Binding.DisplayId,
                        restoreBounds);
                    _snapRuntimeBounds[match.Window.Handle] = region.Bounds;
                    _snapWindowInfoCache[match.Window.Handle] = match.Window;
                    ScheduleLaunchedWindowSnapStabilization(
                        match.Window.Handle,
                        match.Binding.DisplayId,
                        match.Binding.NodeId,
                        region.Bounds);
                    restoredCount++;
                    restoredMatches.Add(match);
                }
            }
        }
        finally
        {
            EndInternalWindowLayoutUpdate();
        }

        RestoreWorkspaceWindowStackOrder(restoredMatches);
        return restoredCount;
    }

}
