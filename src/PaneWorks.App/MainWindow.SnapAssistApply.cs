using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void FinalizeMovingWindowAfterRelease(string reason)
    {
        if (_movingWindowHandle == IntPtr.Zero)
        {
            return;
        }

        if (_movingWindowDetachedFromSnapGroup)
        {
            RestoreDetachedWindowAfterMove();
        }
        else if (TryResolveTemporarySnapTargetForRelease(out var temporaryTarget)
                 && TryApplyTemporarySnapInsertion(temporaryTarget, reason))
        {
            // The insertion method updates the in-memory session, all affected windows,
            // and the new binding as one atomic runtime operation.
        }
        else if (TryResolveSnapTargetForRelease(out var snapRegion, out var snapDisplayId))
        {
            var restoreBounds = ResolveRestoreBoundsForSnap(_movingWindowHandle);
            PaneWorksLog.Info($"Snap window ({reason}): 0x{_movingWindowHandle.ToInt64():X}, restore={restoreBounds.Width:0}x{restoreBounds.Height:0}");
            _snapBindings[_movingWindowHandle] = new SnapBindingState(snapRegion.NodeId, snapDisplayId, restoreBounds);
            _snapRuntimeBounds[_movingWindowHandle] = snapRegion.Bounds;
            if (TrySnapWindowToBoundsWithStatus(_movingWindowHandle, snapRegion.Bounds, reason))
            {
                BringSnappedWindowToTopSoon(_movingWindowHandle);
                QueueSnappedWindowInfoCache(_movingWindowHandle);
            }
        }
        else if (_movingWindowSnapResizeGesture)
        {
            PaneWorksLog.Info($"Finalize snapped runtime resize without linked session ({reason}): 0x{_movingWindowHandle.ToInt64():X}");
            CaptureCurrentRuntimeBoundsForDisplay(null);
            TryUpdateSessionSnapLayoutFromWindowBounds(_movingWindowHandle);
        }
        else if (!_movingWindowDetachedFromSnapGroup && _snapBindings.ContainsKey(_movingWindowHandle))
        {
            PaneWorksLog.Info($"Finalize snapped resize into session layout ({reason}): 0x{_movingWindowHandle.ToInt64():X}");
            CaptureCurrentRuntimeBoundsForDisplay(null);
            TryUpdateSessionSnapLayoutFromWindowBounds(_movingWindowHandle);
        }
    }

    private bool TrySnapWindowToBoundsWithStatus(IntPtr windowHandle, PaneRect bounds, string reason)
    {
        if (_workspaceApplyService.TrySnapWindowToBounds(windowHandle, bounds, out var errorCode))
        {
            return true;
        }

        PaneWorksLog.Info($"Snap window failed ({reason}): 0x{windowHandle.ToInt64():X}, error={errorCode}");
        ViewModel.SetUserStatusMessage(errorCode == Win32ErrorAccessDenied
            ? "这个窗口权限高于 PaneWorks。请用管理员身份启动 PaneWorks 后再吸附任务管理器或管理员软件。"
            : $"窗口吸附被系统拒绝，错误码：{errorCode}");
        return false;
    }

    private void BringSnappedWindowToTopSoon(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        _workspaceApplyService.BringWindowToTop(windowHandle);
        _ = Task.Delay(120).ContinueWith(
            _ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_snapBindings.ContainsKey(windowHandle))
                    {
                        _workspaceApplyService.BringWindowToTop(windowHandle);
                    }
                });
            },
            TaskScheduler.Default);
    }
}
