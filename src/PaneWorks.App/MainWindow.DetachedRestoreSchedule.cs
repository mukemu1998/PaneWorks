using System.Windows.Threading;
using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void ScheduleDetachedWindowRestore(IntPtr windowHandle, PaneRect restoreBounds)
    {
        RestoreDetachedWindowIfIdle(windowHandle, restoreBounds, "immediate");

        ScheduleDelayedDetachedWindowRestore(windowHandle, restoreBounds, TimeSpan.FromMilliseconds(80), "delay-80");
        ScheduleDelayedDetachedWindowRestore(windowHandle, restoreBounds, TimeSpan.FromMilliseconds(220), "delay-220");
    }

    private void ScheduleDetachedWindowRestoreDuringMove(IntPtr windowHandle, PaneRect restoreBounds)
    {
        ScheduleDelayedDetachedWindowRestoreDuringMove(windowHandle, restoreBounds, TimeSpan.FromMilliseconds(35), "move-delay-35");
        ScheduleDelayedDetachedWindowRestoreDuringMove(windowHandle, restoreBounds, TimeSpan.FromMilliseconds(90), "move-delay-90");
    }

    private void ScheduleDelayedDetachedWindowRestoreDuringMove(IntPtr windowHandle, PaneRect restoreBounds, TimeSpan delay, string phase)
    {
        var timer = new DispatcherTimer
        {
            Interval = delay
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RestoreDetachedWindowWhileMoving(windowHandle, restoreBounds, phase);
        };

        timer.Start();
    }

    private void RestoreDetachedWindowWhileMoving(IntPtr windowHandle, PaneRect restoreBounds, string phase)
    {
        if (windowHandle == IntPtr.Zero
            || _movingWindowHandle != windowHandle
            || !_movingWindowDetachedFromSnapGroup)
        {
            return;
        }

        var finalBounds = GetDetachedRestoreBounds(windowHandle, restoreBounds);
        PaneWorksLog.Info($"Apply detached restore {phase}: 0x{windowHandle.ToInt64():X}, restore={finalBounds.Width:0}x{finalBounds.Height:0}");
        _workspaceApplyService.RestoreWindowToBounds(windowHandle, finalBounds);
    }

    private void ScheduleDelayedDetachedWindowRestore(IntPtr windowHandle, PaneRect restoreBounds, TimeSpan delay, string phase)
    {
        var timer = new DispatcherTimer
        {
            Interval = delay
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RestoreDetachedWindowIfIdle(windowHandle, restoreBounds, phase);
        };

        timer.Start();
    }

    private void RestoreDetachedWindowIfIdle(IntPtr windowHandle, PaneRect restoreBounds, string phase)
    {
        if (windowHandle == IntPtr.Zero || _movingWindowHandle == windowHandle)
        {
            return;
        }

        var finalBounds = GetDetachedRestoreBounds(windowHandle, restoreBounds);
        PaneWorksLog.Info($"Apply detached restore {phase}: 0x{windowHandle.ToInt64():X}, restore={finalBounds.Width:0}x{finalBounds.Height:0}");
        _workspaceApplyService.RestoreWindowToBounds(windowHandle, finalBounds);
    }
}
