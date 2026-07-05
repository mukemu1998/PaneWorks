using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void RestoreWorkspaceWindowStackOrder(IReadOnlyList<MatchedWindowBinding> matches)
    {
        if (matches.Count <= 1)
        {
            return;
        }

        foreach (var group in matches.GroupBy(match => GetBindingKey(match.Binding), StringComparer.OrdinalIgnoreCase))
        {
            var windowHandles = group
                .OrderByDescending(match => match.Binding.StackOrder)
                .Select(match => match.Window.Handle)
                .ToList();
            _workspaceApplyService.ArrangeWindowsTopToBottom(windowHandles);
        }
    }

    private void RestoreRuntimeWorkspaceStackOrder(IReadOnlyList<WorkspaceWindowBinding> bindings)
    {
        if (bindings.Count <= 1)
        {
            return;
        }

        foreach (var group in bindings.GroupBy(GetBindingKey, StringComparer.OrdinalIgnoreCase))
        {
            var windowHandles = new List<IntPtr>();
            foreach (var binding in group.OrderByDescending(item => item.StackOrder))
            {
                if (TryGetLiveRuntimeBindingHandle(binding, out var windowHandle))
                {
                    windowHandles.Add(windowHandle);
                }
            }

            _workspaceApplyService.ArrangeWindowsTopToBottom(windowHandles);
        }
    }

    private bool TryGetLiveRuntimeBindingHandle(WorkspaceWindowBinding binding, out IntPtr windowHandle)
    {
        windowHandle = IntPtr.Zero;
        var staleHandles = new List<IntPtr>();
        var candidates = _snapBindings
            .Where(item =>
                string.Equals(item.Value.DisplayId, binding.DisplayId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Value.NodeId, binding.NodeId, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Key)
            .ToList();

        foreach (var candidateHandle in candidates)
        {
            if (!_workspaceApplyService.TryGetVisibleWindowBounds(candidateHandle, out _))
            {
                staleHandles.Add(candidateHandle);
                continue;
            }

            if (!_snapWindowInfoCache.TryGetValue(candidateHandle, out var windowInfo))
            {
                QueueSnappedWindowInfoCache(candidateHandle);
                continue;
            }

            if (IsExplorerFolderBinding(binding)
                && IsExplorerProcess(windowInfo.ProcessName)
                && string.IsNullOrWhiteSpace(windowInfo.ExplorerFolderPath)
                && _workspaceApplyService.TryGetExplorerFolderPath(candidateHandle, out var folderPath))
            {
                windowInfo = windowInfo with { ExplorerFolderPath = NormalizeFolderPath(folderPath) };
                _snapWindowInfoCache[candidateHandle] = windowInfo;
            }

            if (ScoreWindowBinding(binding, windowInfo) <= 0)
            {
                continue;
            }

            windowHandle = candidateHandle;
            break;
        }

        foreach (var staleHandle in staleHandles)
        {
            _snapBindings.Remove(staleHandle);
            _snapRuntimeBounds.Remove(staleHandle);
            _snapWindowInfoCache.Remove(staleHandle);
        }

        return windowHandle != IntPtr.Zero;
    }

    private static List<MatchedWindowBinding> MatchWindowBindings(
        IReadOnlyList<WorkspaceWindowBinding> bindings,
        IReadOnlyList<VisibleWindowInfo> windows)
    {
        var remainingWindows = windows.ToList();
        var matches = new List<MatchedWindowBinding>();

        foreach (var binding in bindings)
        {
            var match = remainingWindows
                .Select(window => new
                {
                    Window = window,
                    Score = ScoreWindowBinding(binding, window)
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Window.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Window.Title, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (match is null)
            {
                continue;
            }

            matches.Add(new MatchedWindowBinding(binding, match.Window));
            remainingWindows.Remove(match.Window);
        }

        return matches;
    }

    private sealed record MatchedWindowBinding(WorkspaceWindowBinding Binding, VisibleWindowInfo Window);
}
