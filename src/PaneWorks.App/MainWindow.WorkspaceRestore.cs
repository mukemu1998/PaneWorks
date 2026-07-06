using System.Windows;
using System.Windows.Interop;
using PaneWorks.App.Diagnostics;
using PaneWorks.App.Views;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private async void RestoreBoundWindowsForActiveWorkspaceProfile(bool clearRuntimeState, bool notifyOnResult, string reason)
    {
        if (!ViewModel.IsWorkspaceProfileEnabled)
        {
            if (notifyOnResult)
            {
                PaneMessageService.Show(
                    this,
                    "请先启用一套工作区方案，再恢复绑定窗口。",
                    buttons: MessageBoxButton.OK,
                    image: MessageBoxImage.Information);
            }

            return;
        }

        var bindings = ViewModel.GetActiveWorkspaceWindowBindings();
        if (bindings.Count == 0)
        {
            if (clearRuntimeState)
            {
                ClearSnapRuntimeCollections();
            }

            if (notifyOnResult)
            {
                PaneMessageService.Show(
                    this,
                    "当前工作区方案还没有保存任何窗口绑定。",
                    buttons: MessageBoxButton.OK,
                    image: MessageBoxImage.Information);
            }

            return;
        }

        var excludedWindowHandle = new WindowInteropHelper(this).Handle;
        var visibleWindows = _workspaceApplyService
            .GetVisibleWindows(excludedWindowHandle)
            .ToList();

        var matches = MatchWindowBindings(bindings, visibleWindows);

        if (clearRuntimeState)
        {
            ClearSnapRuntimeCollections();
        }

        var restoredCount = 0;
        var regionMissingCount = 0;
        var snapRejectedCount = 0;
        var issues = new List<WorkspaceRestoreIssue>();
        var restoredMatches = new List<MatchedWindowBinding>();
        var geometryByDisplay = new Dictionary<string, IReadOnlyDictionary<string, ComputedRegion>>(StringComparer.OrdinalIgnoreCase);

        BeginInternalWindowLayoutUpdate();
        try
        {
            foreach (var match in matches)
            {
                var regionResult = ResolveBindingRegion(match.Binding, geometryByDisplay);
                if (!regionResult.Success || regionResult.Region is null)
                {
                    regionMissingCount++;
                    issues.Add(new WorkspaceRestoreIssue(
                        FormatWorkspaceBindingTarget(match.Binding),
                        ResolveBindingRegionFailureMessage(match.Binding, regionResult.Failure)));
                    continue;
                }

                var region = regionResult.Region;
                var restoreBounds = ResolveRestoreBoundsForSnap(match.Window.Handle);
                if (TrySnapWindowToBoundsWithStatus(match.Window.Handle, region.Bounds, "workspace-restore"))
                {
                    _snapBindings[match.Window.Handle] = new SnapBindingState(
                        match.Binding.NodeId,
                        match.Binding.DisplayId,
                        restoreBounds);
                    _snapRuntimeBounds[match.Window.Handle] = region.Bounds;
                    _snapWindowInfoCache[match.Window.Handle] = match.Window;
                    restoredCount++;
                    restoredMatches.Add(match);
                }
                else
                {
                    snapRejectedCount++;
                    issues.Add(new WorkspaceRestoreIssue(
                        FormatWorkspaceBindingTarget(match.Binding),
                        "系统拒绝吸附，可能是权限不足、窗口不允许移动或窗口处于特殊状态。"));
                }
            }
        }
        finally
        {
            EndInternalWindowLayoutUpdate();
        }

        RestoreWorkspaceWindowStackOrder(restoredMatches);

        var matchedBindingKeys = matches
            .Select(match => GetBindingInstanceKey(match.Binding))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingBindings = bindings
            .Where(binding => !matchedBindingKeys.Contains(GetBindingInstanceKey(binding)))
            .ToList();
        var launchResult = LaunchMissingWorkspaceWindows(missingBindings);
        var launchedRestoredCount = 0;
        if (launchResult.StartedCount > 0)
        {
            ViewModel.SetUserStatusMessage($"已启动 {launchResult.StartedCount} 个缺失窗口，正在等待窗口就绪并吸附...");
            try
            {
                launchedRestoredCount = await RestoreLaunchedWorkspaceWindowsAsync(missingBindings, excludedWindowHandle);
            }
            catch (Exception exception)
            {
                PaneWorksLog.Error("Restore launched workspace windows failed", exception);
            }
        }

        restoredCount += launchedRestoredCount;
        issues.AddRange(launchResult.SkippedIssues);
        issues.AddRange(launchResult.FailedIssues);
        var startedButNotRestoredBindings = launchResult.StartedBindings
            .Where(binding => !TryGetLiveRuntimeBindingHandle(binding, out _))
            .ToList();
        var startedButNotRestoredCount = startedButNotRestoredBindings.Count;
        foreach (var binding in startedButNotRestoredBindings)
        {
            issues.Add(new WorkspaceRestoreIssue(
                FormatWorkspaceBindingTarget(binding),
                "已尝试启动，但暂未识别到可吸附窗口。大型软件可能还在加载，稍后可再次应用工作区。"));
        }

        RestoreRuntimeWorkspaceStackOrder(bindings);
        ScheduleWorkspaceStackOrderStabilization(bindings, reason);
        if (launchedRestoredCount > 0)
        {
            ViewModel.SetUserStatusMessage($"已等待并吸附 {launchedRestoredCount} 个刚启动的窗口。");
        }

        var summary = new WorkspaceRestoreSummary(
            bindings.Count,
            matches.Count,
            restoredCount,
            launchResult.StartedCount,
            launchResult.SkippedCount,
            launchResult.FailedCount,
            regionMissingCount,
            snapRejectedCount,
            startedButNotRestoredCount,
            issues);
        ViewModel.SetUserStatusMessage(summary.ToStatusMessage());
        var restoreReport = summary.ToDialogMessage();
        PaneWorksDiagnosticState.SetLastWorkspaceRestoreReport(restoreReport);

        PaneWorksLog.Info(
            $"Window binding restore: reason={reason}, bindings={bindings.Count}, matched={matches.Count}, "
            + $"started={launchResult.StartedCount}, launchSkipped={launchResult.SkippedCount}, launchFailed={launchResult.FailedCount}, "
            + $"regionMissing={regionMissingCount}, snapRejected={snapRejectedCount}, startedButNotRestored={startedButNotRestoredCount}, restored={restoredCount}");
        foreach (var issue in issues)
        {
            PaneWorksLog.Info($"Workspace restore issue: {issue.Target} | {issue.Reason}");
        }

        if (notifyOnResult)
        {
            PaneMessageService.Show(
                this,
                restoreReport,
                buttons: MessageBoxButton.OK,
                image: summary.ProblemCount == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }

}
