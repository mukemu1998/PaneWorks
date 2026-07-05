using System.Windows.Threading;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void SwitchSnapLayoutAndResetRuntimeState(string layoutId)
    {
        ViewModel.SwitchSnapLayout(layoutId, notifyOnSuccess: false);
        ResetSnapRuntimeStateAfterLayoutSwitch();
    }

    private void SwitchWorkspaceProfileAndResetRuntimeState(string profileId, bool notifyOnSuccess)
    {
        if (!ViewModel.TrySwitchWorkspaceProfile(profileId, notifyOnSuccess))
        {
            return;
        }

        ResetSnapRuntimeStateAfterLayoutSwitch();
        Dispatcher.BeginInvoke(
            () => RestoreBoundWindowsForActiveWorkspaceProfile(clearRuntimeState: false, notifyOnResult: notifyOnSuccess, reason: "workspace-profile-switch"),
            DispatcherPriority.Background);
    }

    private void ResetSnapRuntimeStateAfterLayoutSwitch()
    {
        StopManualDetachedDragLoop();
        StopRuntimeLinkedResizeLoop();
        _snapAssistTimer.Stop();
        _snapBindings.Clear();
        _snapRuntimeBounds.Clear();
        _snapWindowInfoCache.Clear();
        _sessionSnapLayoutDocuments.Clear();
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
        _movingWindowSnapResizeGesture = false;
        EnsureSnapOverlayHidden();
    }
}
