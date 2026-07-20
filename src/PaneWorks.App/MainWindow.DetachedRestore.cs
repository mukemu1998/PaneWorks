using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void DetachMovingWindowFromSnapGroup()
    {
        if (_movingWindowHandle == IntPtr.Zero || _movingWindowDetachedFromSnapGroup)
        {
            return;
        }

        _movingWindowDetachedFromSnapGroup = true;
        _movingWindowDetachCandidate = false;
        _detachedRestoreSettled = false;
        _lastDetachedRestoreApplyAt = DateTimeOffset.MinValue;
        _snapSuppressUntilByWindow[_movingWindowHandle] = DateTimeOffset.UtcNow.AddMilliseconds(700);
        var hasBinding = _snapBindings.TryGetValue(_movingWindowHandle, out var binding);
        _snapBindings.Remove(_movingWindowHandle);
        _snapRuntimeBounds.Remove(_movingWindowHandle);
        _snapWindowInfoCache.Remove(_movingWindowHandle);
        if (hasBinding)
        {
            PaneWorksLog.Info($"Detach snapped window and restore: 0x{_movingWindowHandle.ToInt64():X}");
            var restoreBounds = GetDetachedRestoreBounds(_movingWindowHandle, binding!.RestoreBounds);
            _pendingDetachedRestoreBounds = restoreBounds;
            _manualDetachedDragActive = true;
            _manualDetachedDragLastBounds = null;
            _workspaceApplyService.CancelWindowDrag(_movingWindowHandle);
            _workspaceApplyService.RestoreWindowToBounds(_movingWindowHandle, restoreBounds);
            _manualDetachedDragFrameAdjustment = _workspaceApplyService.GetWindowFrameAdjustment(_movingWindowHandle);
            StartManualDetachedDragLoop(_movingWindowHandle, restoreBounds, _manualDetachedDragFrameAdjustment);
        }
        else
        {
            _workspaceApplyService.RestoreRoundedCorners(_movingWindowHandle);
        }
        ReapplySnapBindings();
    }

    private void RestoreDetachedWindowAfterMove()
    {
        if (_movingWindowHandle == IntPtr.Zero || _pendingDetachedRestoreBounds is null)
        {
            return;
        }

        if (_workspaceApplyService.UsesConservativeSnapHandling(_movingWindowHandle))
        {
            // The initial detach already restored the original bounds. Reapplying it
            // after the native drag ends can break an accelerated viewport again.
            _pendingDetachedRestoreBounds = null;
            return;
        }

        var restoreBounds = GetDetachedRestoreBounds(_movingWindowHandle, _pendingDetachedRestoreBounds.Value);
        PaneWorksLog.Info($"Finalize detached restore: 0x{_movingWindowHandle.ToInt64():X}, restore={restoreBounds.Width:0}x{restoreBounds.Height:0}");
        ScheduleDetachedWindowRestore(_movingWindowHandle, restoreBounds);
    }

    private void MaintainDetachedWindowRestoreDuringMove()
    {
        if (_movingWindowHandle == IntPtr.Zero || _pendingDetachedRestoreBounds is null || _detachedRestoreSettled)
        {
            return;
        }

        var restoreBounds = GetDetachedRestoreBounds(_movingWindowHandle, _pendingDetachedRestoreBounds.Value);
        if (_workspaceApplyService.TryGetVisibleWindowBounds(_movingWindowHandle, out var currentBounds)
            && IsSizeClose(currentBounds, restoreBounds, WindowBoundsTolerance))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastDetachedRestoreApplyAt < DetachedRestoreApplyInterval)
        {
            return;
        }

        _lastDetachedRestoreApplyAt = now;
        _workspaceApplyService.RestoreWindowToBounds(_movingWindowHandle, restoreBounds);
    }

    private bool RecoverStaleMoveStateIfNeeded()
    {
        if (_movingWindowHandle == IntPtr.Zero || _movingWindowStartedAt is null)
        {
            return false;
        }

        if (IsPrimaryMouseButtonPressed())
        {
            _movingWindowMouseReleasedAt = null;
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        _movingWindowMouseReleasedAt ??= now;
        var settleDelay = _movingWindowStartedByForegroundFallback
            ? TimeSpan.FromMilliseconds(80)
            : TimeSpan.FromMilliseconds(800);
        if (now - _movingWindowMouseReleasedAt.Value < settleDelay)
        {
            return false;
        }

        PaneWorksLog.Info($"Recover stale move state: 0x{_movingWindowHandle.ToInt64():X}");
        _snapAssistTimer.Stop();
        FinalizeMovingWindowAfterRelease(_movingWindowStartedByForegroundFallback ? "foreground-fallback" : "stale");
        ResetMovingWindowState();
        EnsureSnapOverlayHidden();
        return true;
    }

    private PaneRect GetDetachedRestoreBounds(IntPtr windowHandle, PaneRect savedBounds)
    {
        var restoreWidth = Math.Max(120, savedBounds.Width);
        var restoreHeight = Math.Max(80, savedBounds.Height);

        if (TryGetCursorPosition(out var cursor))
        {
            return GetDetachedRestoreBoundsFromCursor(
                cursor,
                new PaneRect(savedBounds.X, savedBounds.Y, restoreWidth, restoreHeight),
                _movingWindowDragAnchorRatioX,
                _movingWindowDragAnchorOffsetY);
        }

        if (!_workspaceApplyService.TryGetVisibleWindowBounds(windowHandle, out var currentBounds))
        {
            return savedBounds;
        }

        return new PaneRect(
            currentBounds.X,
            currentBounds.Y,
            restoreWidth,
            restoreHeight);
    }

    private static PaneRect GetDetachedRestoreBoundsFromCursor(
        WpfPoint cursor,
        PaneRect savedBounds,
        double anchorRatioX,
        double anchorOffsetY)
    {
        var restoreWidth = Math.Max(120, savedBounds.Width);
        var restoreHeight = Math.Max(80, savedBounds.Height);
        var anchorX = Math.Clamp(anchorRatioX, DragAnchorMinRatio, DragAnchorMaxRatio) * restoreWidth;
        var anchorY = Math.Clamp(
            anchorOffsetY,
            DragAnchorMinOffsetY,
            Math.Min(DragAnchorMaxOffsetY, Math.Max(DragAnchorMinOffsetY, restoreHeight - 8)));

        return new PaneRect(
            cursor.X - anchorX,
            cursor.Y - anchorY,
            restoreWidth,
            restoreHeight);
    }

    private void CaptureMovingWindowDragAnchor(PaneRect? bounds)
    {
        _movingWindowDragAnchorRatioX = 0.5;
        _movingWindowDragAnchorOffsetY = 24;

        if (bounds is null || bounds.Value.Width <= 0 || bounds.Value.Height <= 0 || !TryGetCursorPosition(out var cursor))
        {
            return;
        }

        _movingWindowDragAnchorRatioX = Math.Clamp(
            (cursor.X - bounds.Value.X) / bounds.Value.Width,
            DragAnchorMinRatio,
            DragAnchorMaxRatio);
        _movingWindowDragAnchorOffsetY = Math.Clamp(
            cursor.Y - bounds.Value.Y,
            DragAnchorMinOffsetY,
            Math.Min(DragAnchorMaxOffsetY, Math.Max(DragAnchorMinOffsetY, bounds.Value.Height - 8)));
    }

    private void ResetMovingWindowState()
    {
        StopManualDetachedDragLoop();
        StopRuntimeLinkedResizeLoop();
        _snapAssistTimer.Stop();
        _movingWindowHandle = IntPtr.Zero;
        _hoveredSnapRegion = null;
        _hoveredSnapDisplayId = null;
        ClearSnapAssistTargetMemory();
        _movingWindowInitialBounds = null;
        _pendingDetachedRestoreBounds = null;
        _movingWindowStartedAt = null;
        _movingWindowMouseReleasedAt = null;
        _lastDetachedRestoreApplyAt = DateTimeOffset.MinValue;
        _manualDetachedDragLastBounds = null;
        _manualDetachedDragFrameAdjustment = default;
        _runtimeLinkedResizeSession = null;
        _detachedRestoreSettled = false;
        _manualDetachedDragActive = false;
        _runtimeLinkedResizeActive = false;
        _movingWindowWasSnapped = false;
        _movingWindowDetachedFromSnapGroup = false;
        _movingWindowDetachCandidate = false;
        _movingWindowSnapResizeGesture = false;
        _movingWindowStartedByForegroundFallback = false;
    }

}
