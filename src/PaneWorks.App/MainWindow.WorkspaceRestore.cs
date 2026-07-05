using System.Windows;
using System.Windows.Interop;
using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace PaneWorks.App;

public partial class MainWindow
{
    private async void RestoreBoundWindowsForActiveWorkspaceProfile(bool clearRuntimeState, bool notifyOnResult, string reason)
    {
        if (!ViewModel.IsWorkspaceProfileEnabled)
        {
            if (notifyOnResult)
            {
                WpfMessageBox.Show(
                    this,
                    "请先启用一套工作区方案，再恢复绑定窗口。",
                    "PaneWorks",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return;
        }

        var bindings = ViewModel.GetActiveWorkspaceWindowBindings();
        if (bindings.Count == 0)
        {
            if (clearRuntimeState)
            {
                _snapBindings.Clear();
                _snapRuntimeBounds.Clear();
                _snapWindowInfoCache.Clear();
                _sessionSnapLayoutDocuments.Clear();
            }

            if (notifyOnResult)
            {
                WpfMessageBox.Show(
                    this,
                    "当前工作区方案还没有保存任何窗口绑定。",
                    "PaneWorks",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
            _snapBindings.Clear();
            _snapRuntimeBounds.Clear();
            _snapWindowInfoCache.Clear();
            _sessionSnapLayoutDocuments.Clear();
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
                _snapBindings[match.Window.Handle] = new SnapBindingState(
                    match.Binding.NodeId,
                    match.Binding.DisplayId,
                    restoreBounds);
                _snapRuntimeBounds[match.Window.Handle] = region.Bounds;
                _snapWindowInfoCache[match.Window.Handle] = match.Window;
                _ = TrySnapWindowToBoundsWithStatus(match.Window.Handle, region.Bounds, "workspace-restore");
                restoredCount++;
                restoredMatches.Add(match);
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
        var launchedCount = LaunchMissingWorkspaceWindows(missingBindings);
        var launchedRestoredCount = 0;
        if (launchedCount > 0)
        {
            ViewModel.SetUserStatusMessage($"已启动 {launchedCount} 个缺失窗口，正在等待窗口就绪并吸附...");
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
        RestoreRuntimeWorkspaceStackOrder(bindings);
        ScheduleWorkspaceStackOrderStabilization(bindings, reason);
        if (launchedRestoredCount > 0)
        {
            ViewModel.SetUserStatusMessage($"已等待并吸附 {launchedRestoredCount} 个刚启动的窗口。");
        }

        PaneWorksLog.Info($"Window binding restore: reason={reason}, bindings={bindings.Count}, matched={matches.Count}, launched={launchedCount}, restored={restoredCount}");

        if (notifyOnResult)
        {
            WpfMessageBox.Show(
                this,
                restoredCount > 0
                    ? launchedCount > 0
                        ? $"已按当前工作区恢复 {restoredCount} 个窗口，其中启动了 {launchedCount} 个未打开窗口。"
                        : $"已按当前工作区重新吸附 {restoredCount} 个已打开窗口。"
                    : "这次没有找到可吸附或可启动的工作区窗口。",
                "PaneWorks",
                MessageBoxButton.OK,
                restoredCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }

    private bool TryGetBindingRegion(
        WorkspaceWindowBinding binding,
        Dictionary<string, IReadOnlyDictionary<string, ComputedRegion>> geometryByDisplay,
        out ComputedRegion region)
    {
        region = default!;

        if (!ViewModel.TryGetDisplayById(binding.DisplayId, out var display))
        {
            return false;
        }

        if (!geometryByDisplay.TryGetValue(display.Id, out var regionsById))
        {
            var geometry = _geometryCalculator.Compute(
                ViewModel.GetSnapLayoutDocumentForDisplay(display.Id),
                GetSnapTargetStageBounds(display),
                SnapTargetSplitterThickness);
            regionsById = geometry.Regions.ToDictionary(item => item.NodeId, StringComparer.Ordinal);
            geometryByDisplay[display.Id] = regionsById;
        }

        if (regionsById is null)
        {
            return false;
        }

        if (!regionsById.TryGetValue(binding.NodeId, out var foundRegion))
        {
            return false;
        }

        region = foundRegion;
        return true;
    }

}
