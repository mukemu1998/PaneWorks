using PaneWorks.App.Diagnostics;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void WindowMoveMonitor_MoveStarted(object? sender, WindowMoveStateChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (!_isSnapAssistArmed || e.WindowHandle == IntPtr.Zero)
                {
                    return;
                }

                if (IsInternalWindowLayoutUpdateActive())
                {
                    PaneWorksLog.Info($"Ignore move start during internal layout update: 0x{e.WindowHandle.ToInt64():X}");
                    return;
                }

                var now = DateTimeOffset.UtcNow;
                if (_lastMoveStartEventHandle == e.WindowHandle
                    && now - _lastMoveStartEventAt < TimeSpan.FromMilliseconds(250))
                {
                    PaneWorksLog.Info($"Duplicate move start ignored: 0x{e.WindowHandle.ToInt64():X}");
                    return;
                }

                _lastMoveStartEventHandle = e.WindowHandle;
                _lastMoveStartEventAt = now;

                if (_movingWindowHandle != IntPtr.Zero)
                {
                    if (_movingWindowHandle == e.WindowHandle
                        && (_snapAssistTimer.IsEnabled || _manualDetachedDragActive))
                    {
                        PaneWorksLog.Info($"Active move start ignored: 0x{e.WindowHandle.ToInt64():X}");
                        return;
                    }

                    PaneWorksLog.Info($"Move state reset before new move: 0x{_movingWindowHandle.ToInt64():X}");
                    ResetMovingWindowState();
                }

                BeginTrackingExternalWindowMove(e.WindowHandle, now, startedByForegroundFallback: false);
            }
            catch (Exception exception)
            {
                PaneWorksLog.Error("Move started handler failed", exception);
                ResetMovingWindowState();
                EnsureSnapOverlayHidden();
            }
        });
    }

    private void BeginTrackingExternalWindowMove(
        IntPtr windowHandle,
        DateTimeOffset startedAt,
        bool startedByForegroundFallback)
    {
        _movingWindowHandle = windowHandle;
        _movingWindowStartedAt = startedAt;
        _movingWindowStartedByForegroundFallback = startedByForegroundFallback;
        PaneWorksLog.Info($"{(startedByForegroundFallback ? "Foreground fallback move started" : "Move started")}: 0x{_movingWindowHandle.ToInt64():X}");
        _hoveredSnapRegion = null;
        _hoveredSnapDisplayId = null;
        ClearSnapAssistTargetMemory();
        _movingWindowInitialBounds = _workspaceApplyService.TryGetVisibleWindowBounds(_movingWindowHandle, out var initialBounds)
            ? initialBounds
            : null;
        CaptureMovingWindowDragAnchor(_movingWindowInitialBounds);
        _movingWindowWasSnapped = _snapBindings.ContainsKey(_movingWindowHandle);
        _movingWindowDetachedFromSnapGroup = false;
        _movingWindowSnapResizeGesture = _movingWindowWasSnapped && IsResizeGesture(_movingWindowInitialBounds);
        _movingWindowDetachCandidate = _movingWindowWasSnapped && !_movingWindowSnapResizeGesture;

        if (_movingWindowDetachCandidate && !startedByForegroundFallback)
        {
            DetachMovingWindowFromSnapGroup();
        }

        if (_movingWindowSnapResizeGesture)
        {
            if (EnableRuntimeLinkedResize)
            {
                StartRuntimeLinkedResizeLoop(_movingWindowHandle);
            }
        }
        else
        {
            _snapAssistTimer.Start();
        }
    }

    private void WindowMoveMonitor_MoveEnded(object? sender, WindowMoveStateChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_movingWindowHandle == IntPtr.Zero || e.WindowHandle != _movingWindowHandle)
                {
                    return;
                }

                if (IsInternalWindowLayoutUpdateActive())
                {
                    PaneWorksLog.Info($"Ignore move end during internal layout update: 0x{e.WindowHandle.ToInt64():X}");
                    return;
                }

                PaneWorksLog.Info($"Move ended: 0x{_movingWindowHandle.ToInt64():X}");

                if (_manualDetachedDragActive && _movingWindowDetachedFromSnapGroup)
                {
                    PaneWorksLog.Info($"Ignore system move end during manual detached drag: 0x{_movingWindowHandle.ToInt64():X}");
                    return;
                }

                if (_runtimeLinkedResizeActive && _movingWindowSnapResizeGesture)
                {
                    PaneWorksLog.Info($"Ignore system move end during runtime linked resize: 0x{_movingWindowHandle.ToInt64():X}");
                    return;
                }

                _snapAssistTimer.Stop();
                FinalizeMovingWindowAfterRelease("system");

                ResetMovingWindowState();
                EnsureSnapOverlayHidden();
            }
            catch (Exception exception)
            {
                PaneWorksLog.Error("Move ended handler failed", exception);
                ResetMovingWindowState();
                EnsureSnapOverlayHidden();
            }
        });
    }
}
