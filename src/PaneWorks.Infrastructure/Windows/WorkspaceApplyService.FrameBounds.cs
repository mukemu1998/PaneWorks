using System.Runtime.InteropServices;
using PaneWorks.Core.Models;

namespace PaneWorks.Infrastructure.Windows;

public sealed partial class WorkspaceApplyService
{
    public WindowFrameAdjustment GetWindowFrameAdjustment(IntPtr windowHandle)
    {
        return TryGetWindowFrameAdjustment(windowHandle, out var frameAdjustment)
            ? frameAdjustment
            : default;
    }

    public WindowMinimumSize GetWindowMinimumVisibleSize(IntPtr windowHandle)
    {
        var minTrackSize = GetWindowMinimumTrackSize(windowHandle);
        var frameAdjustment = GetWindowFrameAdjustment(windowHandle);
        return new WindowMinimumSize(
            Math.Max(1, minTrackSize.Width - frameAdjustment.LeftInset - frameAdjustment.RightInset),
            Math.Max(1, minTrackSize.Height - frameAdjustment.TopInset - frameAdjustment.BottomInset));
    }

    public bool TryGetVisibleWindowBounds(IntPtr windowHandle, out PaneRect bounds)
    {
        bounds = default;
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (TryGetExtendedFrameBounds(windowHandle, out var frameRect))
        {
            bounds = new PaneRect(
                frameRect.Left,
                frameRect.Top,
                Math.Max(0, frameRect.Right - frameRect.Left),
                Math.Max(0, frameRect.Bottom - frameRect.Top));
            return true;
        }

        if (!GetWindowRect(windowHandle, out var windowRect))
        {
            return false;
        }

        bounds = new PaneRect(
            windowRect.Left,
            windowRect.Top,
            Math.Max(0, windowRect.Right - windowRect.Left),
            Math.Max(0, windowRect.Bottom - windowRect.Top));
        return true;
    }

    public void RestoreRoundedCorners(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || UsesConservativeSnapMode(windowHandle))
        {
            return;
        }

        try
        {
            var preference = DwmwcpDefault;
            _ = DwmSetWindowAttribute(windowHandle, DwmaWindowCornerPreference, ref preference, Marshal.SizeOf<int>());
        }
        catch
        {
        }
    }

    private static PaneRect AdjustWindowBoundsForFrame(IntPtr windowHandle, PaneRect targetBounds)
    {
        if (!TryGetWindowFrameAdjustment(windowHandle, out var frameAdjustment))
        {
            return targetBounds;
        }

        return ApplyWindowFrameAdjustment(targetBounds, frameAdjustment);
    }

    private static bool TryGetWindowFrameAdjustment(IntPtr windowHandle, out WindowFrameAdjustment frameAdjustment)
    {
        frameAdjustment = default;
        if (!GetWindowRect(windowHandle, out var windowRect)
            || !TryGetExtendedFrameBounds(windowHandle, out var frameRect))
        {
            return false;
        }

        frameAdjustment = new WindowFrameAdjustment(
            Math.Max(0, frameRect.Left - windowRect.Left),
            Math.Max(0, frameRect.Top - windowRect.Top),
            Math.Max(0, windowRect.Right - frameRect.Right),
            Math.Max(0, windowRect.Bottom - frameRect.Bottom));
        return true;
    }

    private static PaneRect ApplyWindowFrameAdjustment(PaneRect targetBounds, WindowFrameAdjustment frameAdjustment)
    {
        return new PaneRect(
            targetBounds.X - frameAdjustment.LeftInset,
            targetBounds.Y - frameAdjustment.TopInset,
            targetBounds.Width + frameAdjustment.LeftInset + frameAdjustment.RightInset,
            targetBounds.Height + frameAdjustment.TopInset + frameAdjustment.BottomInset);
    }

    private static PaneRect ExpandBoundsForSeamCover(PaneRect bounds)
    {
        return new PaneRect(
            bounds.X - SnapSeamOverlap,
            bounds.Y - SnapSeamOverlap,
            Math.Max(0, bounds.Width + (SnapSeamOverlap * 2)),
            Math.Max(0, bounds.Height + (SnapSeamOverlap * 2)));
    }

    private static WindowMinimumSize GetWindowMinimumTrackSize(IntPtr windowHandle)
    {
        var fallback = new WindowMinimumSize(
            Math.Max(1, GetSystemMetrics(SmCxMinTrack)),
            Math.Max(1, GetSystemMetrics(SmCyMinTrack)));

        if (windowHandle == IntPtr.Zero)
        {
            return fallback;
        }

        var minMaxInfo = new NativeMinMaxInfo
        {
            MinTrackSize = new NativePoint
            {
                X = (int)Math.Round(fallback.Width),
                Y = (int)Math.Round(fallback.Height)
            }
        };

        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMinMaxInfo>());
        try
        {
            Marshal.StructureToPtr(minMaxInfo, buffer, false);
            var result = SendMessageTimeout(
                windowHandle,
                WmGetMinMaxInfo,
                UIntPtr.Zero,
                buffer,
                SmtoAbortIfHung,
                50,
                out _);

            if (result == IntPtr.Zero)
            {
                return fallback;
            }

            minMaxInfo = Marshal.PtrToStructure<NativeMinMaxInfo>(buffer);
            return new WindowMinimumSize(
                Math.Max(fallback.Width, minMaxInfo.MinTrackSize.X),
                Math.Max(fallback.Height, minMaxInfo.MinTrackSize.Y));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
