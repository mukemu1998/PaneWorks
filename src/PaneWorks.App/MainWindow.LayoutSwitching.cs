using System.Windows.Threading;
using PaneWorks.App.Diagnostics;

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
        ClearSnapRuntimeCollections();
        _movingWindowHandle = IntPtr.Zero;
        _hoveredSnapRegion = null;
        _hoveredSnapDisplayId = null;
        _hoveredTemporarySnapTarget = null;
        _lastTemporarySnapTarget = null;
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

    private void ClearSnapRuntimeCollections()
    {
        _snapBindings.Clear();
        _snapRuntimeBounds.Clear();
        _snapWindowInfoCache.Clear();
        _sessionSnapLayoutDocuments.Clear();
    }

    private void ResetSnapSessionButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ResetSnapRuntimeStateAfterLayoutSwitch();
        ViewModel.SetUserStatusMessage("已重置当前会话吸附状态，保存的分区布局和工作区方案不受影响。");
        PaneWorksLog.Info("Snap runtime session reset by user");
    }

    private void PauseSnapAssistButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_isSnapAssistPausedByUser)
        {
            _isSnapAssistPausedByUser = false;
            EnsureSnapAssistStarted();
            PauseSnapAssistButton.Content = "暂停吸附";
            ViewModel.SetUserStatusMessage("已恢复窗口吸附待命。");
            PaneWorksLog.Info("Snap assist resumed by user");
            return;
        }

        _isSnapAssistPausedByUser = true;
        DisarmSnapAssist(restoreWindow: false);
        PauseSnapAssistButton.Content = "恢复吸附";
        ViewModel.SetUserStatusMessage("已暂停窗口吸附。再次点击“恢复吸附”后继续待命。");
        PaneWorksLog.Info("Snap assist paused by user");
    }
}
