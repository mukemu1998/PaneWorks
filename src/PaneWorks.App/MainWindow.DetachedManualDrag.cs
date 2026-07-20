using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void UpdateManualDetachedDrag()
    {
        if (_movingWindowHandle == IntPtr.Zero || _pendingDetachedRestoreBounds is null)
        {
            FinishManualDetachedDrag();
            return;
        }

        if (!IsPrimaryMouseButtonPressed())
        {
            FinishManualDetachedDrag();
            return;
        }

        var targetBounds = GetDetachedRestoreBounds(_movingWindowHandle, _pendingDetachedRestoreBounds.Value);
        if (_manualDetachedDragLastBounds is not null
            && AreBoundsClose(_manualDetachedDragLastBounds.Value, targetBounds, 1))
        {
            return;
        }

        _manualDetachedDragLastBounds = targetBounds;
        _workspaceApplyService.MoveWindowToBounds(_movingWindowHandle, targetBounds, _manualDetachedDragFrameAdjustment);
    }

    private void StartManualDetachedDragLoop(
        IntPtr windowHandle,
        PaneRect restoreBounds,
        WindowFrameAdjustment frameAdjustment)
    {
        StopManualDetachedDragLoop();
        _manualDetachedDragCancellation = new CancellationTokenSource();
        var cancellationToken = _manualDetachedDragCancellation.Token;
        var dragGeneration = Interlocked.Increment(ref _manualDetachedDragGeneration);
        var anchorRatioX = _movingWindowDragAnchorRatioX;
        var anchorOffsetY = _movingWindowDragAnchorOffsetY;
        var usesConservativeHandling = _workspaceApplyService.UsesConservativeSnapHandling(windowHandle);

        _ = Task.Factory.StartNew(
            () => RunManualDetachedDragLoop(
                windowHandle,
                restoreBounds,
                frameAdjustment,
                dragGeneration,
                anchorRatioX,
                anchorOffsetY,
                usesConservativeHandling,
                cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void StopManualDetachedDragLoop()
    {
        if (_manualDetachedDragCancellation is null)
        {
            return;
        }

        _manualDetachedDragCancellation.Cancel();
        _manualDetachedDragCancellation = null;
        _ = Interlocked.Increment(ref _manualDetachedDragGeneration);
    }

    private void RunManualDetachedDragLoop(
        IntPtr windowHandle,
        PaneRect restoreBounds,
        WindowFrameAdjustment frameAdjustment,
        long dragGeneration,
        double anchorRatioX,
        double anchorOffsetY,
        bool usesConservativeHandling,
        CancellationToken cancellationToken)
    {
        PaneRect? lastBounds = null;
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        _ = timeBeginPeriod(1);
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsPrimaryMouseButtonPressed())
            {
                var moved = false;
                if (TryGetCursorPosition(out var cursor))
                {
                    var targetBounds = GetDetachedRestoreBoundsFromCursor(cursor, restoreBounds, anchorRatioX, anchorOffsetY);
                    if (lastBounds is null || !AreBoundsClose(lastBounds.Value, targetBounds, 0.5))
                    {
                        _workspaceApplyService.MoveWindowToBounds(windowHandle, targetBounds, frameAdjustment);
                        lastBounds = targetBounds;
                        moved = true;
                    }
                }

                if (moved)
                {
                    if (usesConservativeHandling)
                    {
                        // Accelerated viewports do not tolerate a continuous stream
                        // of programmatic resizes while being detached.
                        Thread.Sleep(25);
                    }
                    else
                    {
                        WaitForDesktopFrame();
                    }
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }
        catch (Exception exception)
        {
            Dispatcher.BeginInvoke(() => PaneWorksLog.Error("Manual detached drag loop failed", exception));
        }
        finally
        {
            _ = timeEndPeriod(1);
            Dispatcher.BeginInvoke(() =>
            {
                if (_manualDetachedDragGeneration == dragGeneration
                    && _movingWindowHandle == windowHandle
                    && _manualDetachedDragActive)
                {
                    FinishManualDetachedDrag();
                }
            });
        }
    }

    private void FinishManualDetachedDrag()
    {
        if (_movingWindowHandle != IntPtr.Zero)
        {
            PaneWorksLog.Info($"Finish manual detached drag: 0x{_movingWindowHandle.ToInt64():X}");
        }

        ResetMovingWindowState();
        EnsureSnapOverlayHidden();
    }
}
