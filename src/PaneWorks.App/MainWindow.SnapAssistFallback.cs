using PaneWorks.App.Diagnostics;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void SnapAssistHealthTimer_Tick(object? sender, EventArgs e)
    {
        if (_isSnapAssistArmed && !_windowMoveMonitor.IsRunning && !IsInternalWindowLayoutUpdateActive())
        {
            PaneWorksLog.Info("Snap assist hook restarted by health timer");
            _windowMoveMonitor.Start();
        }
    }

    private void SnapAssistFallbackTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (!_isSnapAssistArmed
                || _movingWindowHandle != IntPtr.Zero
                || IsInternalWindowLayoutUpdateActive()
                || GetActiveSnapAssistMode() == SnapAssistMode.None
                || !IsPrimaryMouseButtonPressed())
            {
                return;
            }

            if (!TryGetForegroundSnapCandidate(out var foregroundWindowHandle))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (_lastMoveStartEventHandle == foregroundWindowHandle
                && now - _lastMoveStartEventAt < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            _lastMoveStartEventHandle = foregroundWindowHandle;
            _lastMoveStartEventAt = now;
            BeginTrackingExternalWindowMove(foregroundWindowHandle, now, startedByForegroundFallback: true);
        }
        catch (Exception exception)
        {
            PaneWorksLog.Error("Snap assist foreground fallback failed", exception);
            ResetMovingWindowState();
            EnsureSnapOverlayHidden();
        }
    }

    private bool TryGetForegroundSnapCandidate(out IntPtr windowHandle)
    {
        windowHandle = GetForegroundWindow();
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        windowHandle = GetAncestor(windowHandle, GetAncestorRoot);
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        if (!_workspaceApplyService.TryGetVisibleWindowBounds(windowHandle, out var bounds))
        {
            return false;
        }

        return bounds.Width >= 80 && bounds.Height >= 40;
    }

    private bool HasForegroundFallbackWindowMoved()
    {
        if (!_movingWindowStartedByForegroundFallback || _movingWindowInitialBounds is null)
        {
            return true;
        }

        return _workspaceApplyService.TryGetVisibleWindowBounds(_movingWindowHandle, out var currentBounds)
            && !AreBoundsClose(currentBounds, _movingWindowInitialBounds.Value, WindowBoundsTolerance);
    }
}
