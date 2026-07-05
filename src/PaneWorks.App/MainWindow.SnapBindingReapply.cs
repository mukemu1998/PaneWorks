using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private bool IsInternalWindowLayoutUpdateActive()
    {
        return _internalWindowLayoutUpdateDepth > 0
            || DateTimeOffset.UtcNow <= _suppressMoveEventsUntil;
    }

    private void BeginInternalWindowLayoutUpdate()
    {
        _internalWindowLayoutUpdateDepth++;
        _suppressMoveEventsUntil = DateTimeOffset.UtcNow + InternalLayoutMoveSuppressDuration;
        _snapAssistTimer.Stop();
        if (_windowMoveMonitor.IsRunning)
        {
            PaneWorksLog.Info("Pause snap assist hook for internal layout update");
            _windowMoveMonitor.Stop();
        }
    }

    private void EndInternalWindowLayoutUpdate()
    {
        _internalWindowLayoutUpdateDepth = Math.Max(0, _internalWindowLayoutUpdateDepth - 1);
        _suppressMoveEventsUntil = DateTimeOffset.UtcNow + InternalLayoutMoveSuppressDuration;
        if (_isSnapAssistArmed && !_windowMoveMonitor.IsRunning)
        {
            PaneWorksLog.Info("Resume snap assist hook after internal layout update");
            _windowMoveMonitor.Start();
        }
    }

    private void ReapplySnapBindingsSafely(Action? completed = null)
    {
        var updates = BuildSnapBindingUpdates(skipWindowHandle: null, removeDeadBindings: false);
        PaneWorksLog.Info($"Safe snap binding reapply requested: updates={updates.Count}, bindings={_snapBindings.Count}");
        if (updates.Count == 0)
        {
            completed?.Invoke();
            return;
        }

        BeginInternalWindowLayoutUpdate();
        _ = Task.Run(() =>
        {
            try
            {
                _workspaceApplyService.MoveSnappedWindowsToBounds(updates);
                return (Exception?)null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                EndInternalWindowLayoutUpdate();
                if (task.Result is { } exception)
                {
                    PaneWorksLog.Error("Safe snap binding reapply failed", exception);
                }
                else
                {
                    PaneWorksLog.Info($"Safe snap binding reapply completed: bindings={_snapBindings.Count}");
                }

                completed?.Invoke();
            });
        }, TaskScheduler.Default);
    }

    private List<WindowBoundsUpdate> BuildSnapBindingUpdates(IntPtr? skipWindowHandle, bool removeDeadBindings)
    {
        var updates = new List<WindowBoundsUpdate>();
        if (_snapBindings.Count == 0)
        {
            return updates;
        }

        var deadHandles = new List<IntPtr>();
        var geometryByDisplay = new Dictionary<string, IReadOnlyDictionary<string, ComputedRegion>>(StringComparer.OrdinalIgnoreCase);

        foreach (var binding in _snapBindings)
        {
            if (skipWindowHandle.HasValue && binding.Key == skipWindowHandle.Value)
            {
                continue;
            }

            if (!ViewModel.TryGetDisplayById(binding.Value.DisplayId, out var display))
            {
                deadHandles.Add(binding.Key);
                continue;
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

            if (!regionsById.TryGetValue(binding.Value.NodeId, out var region))
            {
                continue;
            }

            var targetBounds = _snapRuntimeBounds.TryGetValue(binding.Key, out var runtimeBounds)
                ? runtimeBounds
                : region.Bounds;

            if (!_workspaceApplyService.TryGetVisibleWindowBounds(binding.Key, out var currentBounds))
            {
                deadHandles.Add(binding.Key);
                continue;
            }

            if (AreBoundsClose(currentBounds, targetBounds, WindowBoundsTolerance))
            {
                continue;
            }

            updates.Add(new WindowBoundsUpdate(
                binding.Key,
                targetBounds,
                _workspaceApplyService.GetWindowFrameAdjustment(binding.Key)));
        }

        if (removeDeadBindings)
        {
            foreach (var handle in deadHandles)
            {
                _snapBindings.Remove(handle);
                _snapRuntimeBounds.Remove(handle);
                _snapWindowInfoCache.Remove(handle);
            }
        }

        return updates;
    }

    private void ReapplySnapBindings(IntPtr? skipWindowHandle = null)
    {
        var updates = BuildSnapBindingUpdates(skipWindowHandle, removeDeadBindings: true);
        foreach (var update in updates)
        {
            _workspaceApplyService.MoveSnappedWindowToBounds(
                update.WindowHandle,
                update.Bounds,
                update.FrameAdjustment);
        }
    }

    private static bool AreBoundsClose(PaneRect currentBounds, PaneRect targetBounds, double tolerance)
    {
        return Math.Abs(currentBounds.X - targetBounds.X) <= tolerance
            && Math.Abs(currentBounds.Y - targetBounds.Y) <= tolerance
            && IsSizeClose(currentBounds, targetBounds, tolerance);
    }

    private static bool IsSizeClose(PaneRect currentBounds, PaneRect targetBounds, double tolerance)
    {
        return Math.Abs(currentBounds.Width - targetBounds.Width) <= tolerance
            && Math.Abs(currentBounds.Height - targetBounds.Height) <= tolerance;
    }

    private PaneRect ResolveRestoreBoundsForSnap(IntPtr windowHandle)
    {
        if (_snapBindings.TryGetValue(windowHandle, out var existingBinding))
        {
            return existingBinding.RestoreBounds;
        }

        if (_movingWindowInitialBounds.HasValue)
        {
            return _movingWindowInitialBounds.Value;
        }

        return _workspaceApplyService.TryGetVisibleWindowBounds(windowHandle, out var currentBounds)
            ? currentBounds
            : new PaneRect();
    }

    private sealed record SnapBindingState(string NodeId, string DisplayId, PaneRect RestoreBounds);
}
