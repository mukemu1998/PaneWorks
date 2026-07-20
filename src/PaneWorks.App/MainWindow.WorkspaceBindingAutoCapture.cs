using System.Windows.Interop;
using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void QueueSnappedWindowInfoCache(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || _snapWindowInfoCache.ContainsKey(windowHandle))
        {
            return;
        }

        var excludedWindowHandle = new WindowInteropHelper(this).Handle;
        _ = Task.Run(() =>
        {
            try
            {
                return _workspaceApplyService.TryGetVisibleWindowInfo(
                    windowHandle,
                    excludedWindowHandle,
                    includeExplorerFolderPath: false,
                    out var windowInfo)
                    ? windowInfo
                    : null;
            }
            catch
            {
                return null;
            }
        }).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (task.Result is null || !_snapBindings.ContainsKey(windowHandle))
                {
                    return;
                }

                _snapWindowInfoCache[windowHandle] = task.Result;
            });
        }, TaskScheduler.Default);
    }

    private bool TryCaptureSnappedWorkspaceWindowBindingRequests(
        out List<SnappedWorkspaceWindowBindingRequest> requests,
        out string message)
    {
        requests = new List<SnappedWorkspaceWindowBindingRequest>();
        message = string.Empty;

        if (_snapBindings.Count == 0)
        {
            message = "当前还没有已吸附窗口。请先把窗口吸附到分区区域后再一键绑定。";
            return false;
        }

        foreach (var item in _snapBindings.ToList())
        {
            if (!_snapWindowInfoCache.TryGetValue(item.Key, out var windowInfo))
            {
                var excludedWindowHandle = new WindowInteropHelper(this).Handle;
                if (!_workspaceApplyService.TryGetVisibleWindowInfo(
                        item.Key,
                        excludedWindowHandle,
                        includeExplorerFolderPath: true,
                        out windowInfo))
                {
                    QueueSnappedWindowInfoCache(item.Key);
                    continue;
                }

                _snapWindowInfoCache[item.Key] = windowInfo;
            }

            requests.Add(new SnappedWorkspaceWindowBindingRequest(item.Value.DisplayId, item.Value.NodeId, windowInfo, 0));
        }

        if (requests.Count == 0)
        {
            message = "没有找到已缓存的吸附窗口。请把窗口重新吸附一次，或先点“应用选中工作区”后再一键绑定。";
            return false;
        }

        requests = AssignSnappedBindingRequestStackOrder(requests);
        return true;
    }

    private List<WorkspaceWindowBinding> BuildSnappedWorkspaceWindowBindings(
        IReadOnlyList<SnappedWorkspaceWindowBindingRequest> requests,
        IntPtr excludedWindowHandle)
    {
        var bindings = new List<WorkspaceWindowBinding>();
        foreach (var request in requests)
        {
            var windowInfo = request.WindowInfo;
            if (_workspaceApplyService.TryGetVisibleWindowInfo(
                    windowInfo.Handle,
                    excludedWindowHandle,
                    includeExplorerFolderPath: true,
                    out var refreshedWindowInfo))
            {
                // Refresh the capture so automatic binding keeps the executable path used by marker icons.
                windowInfo = refreshedWindowInfo;
            }

            if (IsExplorerProcess(windowInfo.ProcessName)
                && string.IsNullOrWhiteSpace(windowInfo.ExplorerFolderPath)
                && _workspaceApplyService.TryGetExplorerFolderPath(windowInfo.Handle, out var folderPath))
            {
                windowInfo = windowInfo with { ExplorerFolderPath = NormalizeFolderPath(folderPath) };
            }

            bindings.Add(CreateWorkspaceWindowBinding(request.DisplayId, request.NodeId, windowInfo, request.StackOrder));
        }

        return bindings;
    }

    private static List<SnappedWorkspaceWindowBindingRequest> AssignSnappedBindingRequestStackOrder(
        IReadOnlyList<SnappedWorkspaceWindowBindingRequest> requests)
    {
        if (requests.Count <= 1)
        {
            return requests.ToList();
        }

        var zOrderRanks = BuildDesktopZOrderRank();
        var orderedRequests = new List<SnappedWorkspaceWindowBindingRequest>();
        foreach (var group in requests.GroupBy(item => GetBindingKey(item.DisplayId, item.NodeId), StringComparer.OrdinalIgnoreCase))
        {
            var stackOrder = 0;
            foreach (var request in group
                .OrderByDescending(item => GetDesktopZOrderRank(item.WindowInfo.Handle, zOrderRanks))
                .ThenBy(item => item.WindowInfo.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.WindowInfo.Title, StringComparer.OrdinalIgnoreCase))
            {
                orderedRequests.Add(request with { StackOrder = stackOrder });
                stackOrder++;
            }
        }

        return orderedRequests;
    }

    private bool TryGetSnapBindingTargetBounds(
        SnapBindingState binding,
        Dictionary<string, IReadOnlyDictionary<string, ComputedRegion>> geometryByDisplay,
        out PaneRect bounds)
    {
        bounds = default;
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

        if (!regionsById.TryGetValue(binding.NodeId, out var region))
        {
            return false;
        }

        bounds = region.Bounds;
        return true;
    }

    private sealed record SnappedWorkspaceWindowBindingRequest(
        string DisplayId,
        string NodeId,
        VisibleWindowInfo WindowInfo,
        int StackOrder);
}
