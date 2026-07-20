using PaneWorks.Core.Models;

namespace PaneWorks.Infrastructure.Windows;

public sealed partial class WorkspaceApplyService
{
    public void MoveWindowToBounds(IntPtr windowHandle, PaneRect bounds)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var targetBounds = AdjustWindowBoundsForFrame(windowHandle, bounds);

        SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            (int)Math.Round(targetBounds.X),
            (int)Math.Round(targetBounds.Y),
            Math.Max(0, (int)Math.Round(targetBounds.Width)),
            Math.Max(0, (int)Math.Round(targetBounds.Height)),
            SwpNoZOrder | SwpNoActivate);
    }

    public void MoveWindowToBounds(IntPtr windowHandle, PaneRect bounds, WindowFrameAdjustment frameAdjustment)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var targetBounds = ApplyWindowFrameAdjustment(bounds, frameAdjustment);

        SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            (int)Math.Round(targetBounds.X),
            (int)Math.Round(targetBounds.Y),
            Math.Max(0, (int)Math.Round(targetBounds.Width)),
            Math.Max(0, (int)Math.Round(targetBounds.Height)),
            SwpNoZOrder | SwpNoActivate);
    }

    public void MoveSnappedWindowToBounds(IntPtr windowHandle, PaneRect bounds, WindowFrameAdjustment frameAdjustment)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var targetBounds = GetSnappedWindowBounds(windowHandle, bounds, frameAdjustment);

        SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            (int)Math.Round(targetBounds.X),
            (int)Math.Round(targetBounds.Y),
            Math.Max(0, (int)Math.Round(targetBounds.Width)),
            Math.Max(0, (int)Math.Round(targetBounds.Height)),
            SwpNoZOrder | SwpNoActivate);
    }

    public void MoveSnappedWindowsToBounds(IReadOnlyList<WindowBoundsUpdate> updates)
    {
        if (updates.Count == 0)
        {
            return;
        }

        // Real-time renderers such as Marmoset Toolbag can corrupt their swap chain
        // when their HWND is included in one deferred batch with other windows.
        if (updates.Any(update => UsesConservativeSnapMode(update.WindowHandle)))
        {
            foreach (var update in updates)
            {
                MoveSnappedWindowToBounds(update.WindowHandle, update.Bounds, update.FrameAdjustment);
            }

            return;
        }

        var deferHandle = BeginDeferWindowPos(updates.Count);
        if (deferHandle != IntPtr.Zero)
        {
            foreach (var update in updates)
            {
                if (update.WindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                var targetBounds = GetSnappedWindowBounds(
                    update.WindowHandle,
                    update.Bounds,
                    update.FrameAdjustment);
                deferHandle = DeferWindowPos(
                    deferHandle,
                    update.WindowHandle,
                    IntPtr.Zero,
                    (int)Math.Round(targetBounds.X),
                    (int)Math.Round(targetBounds.Y),
                    Math.Max(0, (int)Math.Round(targetBounds.Width)),
                    Math.Max(0, (int)Math.Round(targetBounds.Height)),
                    SwpNoZOrder | SwpNoActivate);

                if (deferHandle == IntPtr.Zero)
                {
                    break;
                }
            }

            if (deferHandle != IntPtr.Zero && EndDeferWindowPos(deferHandle))
            {
                return;
            }
        }

        foreach (var update in updates)
        {
            MoveSnappedWindowToBounds(update.WindowHandle, update.Bounds, update.FrameAdjustment);
        }
    }

    public void BringWindowToTop(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public void ArrangeWindowsTopToBottom(IReadOnlyList<IntPtr> windowHandles)
    {
        var previousWindowHandle = IntPtr.Zero;
        foreach (var windowHandle in windowHandles.Where(handle => handle != IntPtr.Zero))
        {
            SetWindowPos(
                windowHandle,
                previousWindowHandle,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate);
            previousWindowHandle = windowHandle;
        }
    }

    public void RestartWindowDragFromCaption(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        _ = SendMessage(windowHandle, WmCancelMode, UIntPtr.Zero, IntPtr.Zero);
        _ = ReleaseCapture();
        var lParam = TryGetCursorPosition(out var cursor)
            ? MakePointLParam(cursor.X, cursor.Y)
            : IntPtr.Zero;
        _ = PostMessage(windowHandle, WmNcLButtonDown, HtCaption, lParam);
    }

    public void CancelWindowDrag(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        _ = SendMessage(windowHandle, WmCancelMode, UIntPtr.Zero, IntPtr.Zero);
        _ = ReleaseCapture();
    }
}
