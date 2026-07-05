using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void ScheduleLaunchedWorkspaceWindowHandoffWatch(
        IReadOnlyList<WorkspaceWindowBinding> bindings,
        IntPtr excludedWindowHandle,
        bool includeExplorerFolderPath,
        DateTimeOffset deadline)
    {
        var watchBindings = bindings.ToList();
        if (watchBindings.Count == 0 || DateTimeOffset.UtcNow >= deadline)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var handoffCount = 0;
                while (DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(WorkspaceLaunchRestoreRetryInterval);

                    List<WorkspaceWindowBinding> bindingsToMatch = [];
                    HashSet<IntPtr> snappedHandles = [];

                    await Dispatcher.InvokeAsync(() =>
                    {
                        bindingsToMatch = watchBindings
                            .Where(binding => !TryGetLiveRuntimeBindingHandle(binding, out _))
                            .ToList();
                        snappedHandles = _snapBindings.Keys.ToHashSet();
                    });

                    if (bindingsToMatch.Count == 0)
                    {
                        continue;
                    }

                    var visibleWindows = _workspaceApplyService
                        .GetVisibleWindows(excludedWindowHandle, includeExplorerFolderPath)
                        .Where(window => !snappedHandles.Contains(window.Handle))
                        .ToList();
                    var matches = MatchWindowBindings(bindingsToMatch, visibleWindows);
                    if (matches.Count == 0)
                    {
                        continue;
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        handoffCount += ApplyWorkspaceWindowMatches(matches);
                        RestoreRuntimeWorkspaceStackOrder(watchBindings);
                        ScheduleWorkspaceStackOrderStabilization(watchBindings, "workspace-launch-handoff");
                    });
                }

                if (handoffCount > 0)
                {
                    PaneWorksLog.Info($"Launched workspace handoff finished: restored={handoffCount}");
                }
            }
            catch (Exception exception)
            {
                PaneWorksLog.Error("Launched workspace handoff watch failed", exception);
            }
        });
    }

    private void ScheduleLaunchedWindowSnapStabilization(
        IntPtr windowHandle,
        string displayId,
        string nodeId,
        PaneRect targetBounds)
    {
        foreach (var delay in WorkspaceLaunchSnapStabilizationDelays)
        {
            _ = Task.Delay(delay).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (!_snapBindings.TryGetValue(windowHandle, out var binding)
                            || !string.Equals(binding.DisplayId, displayId, StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(binding.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        if (_snapRuntimeBounds.TryGetValue(windowHandle, out var runtimeBounds)
                            && !AreBoundsClose(runtimeBounds, targetBounds, 0.5))
                        {
                            return;
                        }

                        BeginInternalWindowLayoutUpdate();
                        try
                        {
                            if (!_workspaceApplyService.TrySnapWindowToBounds(windowHandle, targetBounds, out var errorCode))
                            {
                                PaneWorksLog.Info($"Launched window snap stabilization skipped: 0x{windowHandle.ToInt64():X}, error={errorCode}");
                                return;
                            }
                        }
                        finally
                        {
                            EndInternalWindowLayoutUpdate();
                        }

                        PaneWorksLog.Info($"Launched window snap stabilized: 0x{windowHandle.ToInt64():X}, delay={delay.TotalSeconds:0}s");
                        RestoreRuntimeWorkspaceStackOrder(ViewModel.GetActiveWorkspaceWindowBindings());
                    }
                    catch (Exception exception)
                    {
                        PaneWorksLog.Error("Launched window snap stabilization failed", exception);
                    }
                });
            }, TaskScheduler.Default);
        }
    }

    private void ScheduleWorkspaceStackOrderStabilization(
        IReadOnlyList<WorkspaceWindowBinding> bindings,
        string reason)
    {
        var bindingsSnapshot = bindings.ToList();
        if (bindingsSnapshot.Count <= 1)
        {
            return;
        }

        foreach (var delay in WorkspaceLaunchSnapStabilizationDelays)
        {
            _ = Task.Delay(delay).ContinueWith(_ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        RestoreRuntimeWorkspaceStackOrder(bindingsSnapshot);
                    }
                    catch (Exception exception)
                    {
                        PaneWorksLog.Error($"Workspace stack order stabilization failed: {reason}", exception);
                    }
                });
            }, TaskScheduler.Default);
        }
    }
}
