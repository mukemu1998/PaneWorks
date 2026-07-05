using System.Windows;
using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void EnsureSnapAssistStarted()
    {
        if (_isSnapAssistArmed)
        {
            return;
        }

        _windowMoveMonitor.Start();
        _isSnapAssistArmed = true;
        PaneWorksLog.Info("Snap assist started");
        EnsureMainWindowCoversVirtualDesktop();
        UpdateEditorStageBounds();
        UpdateWorkbenchPanelPosition();
        _snapAssistHealthTimer.Start();
        _snapAssistFallbackTimer.Start();
    }

    private void DisarmSnapAssist(bool restoreWindow)
    {
        _isSnapAssistArmed = false;
        _snapAssistHealthTimer.Stop();
        _snapAssistFallbackTimer.Stop();
        _windowMoveMonitor.Stop();
        PaneWorksLog.Info("Snap assist stopped");
        StopManualDetachedDragLoop();
        StopRuntimeLinkedResizeLoop();
        _snapAssistTimer.Stop();
        _movingWindowHandle = IntPtr.Zero;
        _hoveredSnapRegion = null;
        _hoveredSnapDisplayId = null;
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
        EnsureSnapOverlayHidden();

        if (restoreWindow)
        {
            WindowState = WindowState.Normal;
            EnsureMainWindowCoversVirtualDesktop();
            Activate();
        }
    }

    private void SnapAssistTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (!_isSnapAssistArmed || _movingWindowHandle == IntPtr.Zero)
            {
                return;
            }

            if (_manualDetachedDragActive && _movingWindowDetachedFromSnapGroup)
            {
                _hoveredSnapRegion = null;
                _hoveredSnapDisplayId = null;
                EnsureSnapOverlayHidden();
                return;
            }

            if (RecoverStaleMoveStateIfNeeded())
            {
                return;
            }

            if (_movingWindowDetachedFromSnapGroup)
            {
                _hoveredSnapRegion = null;
                _hoveredSnapDisplayId = null;
                EnsureSnapOverlayHidden();
                return;
            }

            if (IsSnapTemporarilySuppressed(_movingWindowHandle))
            {
                _hoveredSnapRegion = null;
                _hoveredSnapDisplayId = null;
                EnsureSnapOverlayHidden();
                return;
            }

            if (_movingWindowStartedByForegroundFallback && !HasForegroundFallbackWindowMoved())
            {
                _hoveredSnapRegion = null;
                _hoveredSnapDisplayId = null;
                EnsureSnapOverlayHidden();
                return;
            }

            if (_movingWindowStartedByForegroundFallback && _movingWindowDetachCandidate)
            {
                DetachMovingWindowFromSnapGroup();
                return;
            }

            var snapAssistMode = GetActiveSnapAssistMode();
            if (snapAssistMode == SnapAssistMode.None)
            {
                if (!_movingWindowDetachedFromSnapGroup && _snapBindings.ContainsKey(_movingWindowHandle))
                {
                    EnsureSnapOverlayHidden();
                    _movingWindowDetachCandidate = false;
                    return;
                }

                _hoveredSnapRegion = null;
                _hoveredSnapDisplayId = null;
                EnsureSnapOverlayHidden();
                return;
            }
        }
        catch (Exception exception)
        {
            PaneWorksLog.Error("Snap assist timer failed", exception);
            ResetMovingWindowState();
            EnsureSnapOverlayHidden();
            return;
        }

        try
        {
            if (!TryGetCursorPosition(out var cursorInDevicePixels))
            {
                return;
            }

            var activeDisplay = _displayDiscoveryService.GetDisplayFromPoint((int)cursorInDevicePixels.X, (int)cursorInDevicePixels.Y);
            var snapAssistMode = GetActiveSnapAssistMode();
            if (snapAssistMode == SnapAssistMode.None)
            {
                _hoveredSnapRegion = null;
                _hoveredSnapDisplayId = null;
                EnsureSnapOverlayHidden();
                return;
            }

            var snapDocument = GetSnapAssistLayoutDocumentForDisplay(activeDisplay.Id, snapAssistMode);
            var targetStageBounds = GetSnapTargetStageBounds(activeDisplay);
            var targetGeometry = _geometryCalculator.Compute(
                snapDocument,
                targetStageBounds,
                SnapTargetSplitterThickness);

            _hoveredSnapRegion = ResolveHoveredSnapRegion(targetGeometry, activeDisplay.Id, cursorInDevicePixels);
            _hoveredSnapDisplayId = activeDisplay.Id;
            RememberSnapAssistTarget(_hoveredSnapRegion, _hoveredSnapDisplayId);

            EnsureSnapOverlaysVisible(activeDisplay.Id, _hoveredSnapRegion?.NodeId, snapAssistMode);
        }
        catch (Exception exception)
        {
            PaneWorksLog.Error("Snap assist preview failed", exception);
            ResetMovingWindowState();
            EnsureSnapOverlayHidden();
        }
    }

    private bool IsSnapTemporarilySuppressed(IntPtr windowHandle)
    {
        if (!_snapSuppressUntilByWindow.TryGetValue(windowHandle, out var suppressUntil))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow <= suppressUntil)
        {
            return true;
        }

        _snapSuppressUntilByWindow.Remove(windowHandle);
        return false;
    }
}
