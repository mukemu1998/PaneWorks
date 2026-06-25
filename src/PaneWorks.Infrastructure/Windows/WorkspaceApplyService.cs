using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;

namespace PaneWorks.Infrastructure.Windows;

public readonly record struct WindowFrameAdjustment(double LeftInset, double TopInset, double RightInset, double BottomInset);

public readonly record struct WindowBoundsUpdate(IntPtr WindowHandle, PaneRect Bounds, WindowFrameAdjustment FrameAdjustment);

public sealed class WorkspaceApplyService
{
    private const double SnapSeamOverlap = 1;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int DwmaCloaked = 14;
    private const int DwmaExtendedFrameBounds = 9;
    private const int DwmaWindowCornerPreference = 33;
    private const int DwmwcpDefault = 0;
    private const int DwmwcpDoNotRound = 1;
    private const uint WmCancelMode = 0x001F;
    private const uint WmNcLButtonDown = 0x00A1;
    private const nuint HtCaption = 0x0002;

    private readonly LayoutGeometryCalculator _geometryCalculator = new();

    public WorkspaceApplyResult Apply(LayoutDocument document, PaneRect rootBounds, IntPtr excludedWindowHandle)
    {
        var geometry = _geometryCalculator.Compute(document, rootBounds, splitterThickness: 0);

        var orderedRegions = geometry.Regions
            .OrderBy(region => region.Bounds.Y)
            .ThenBy(region => region.Bounds.X)
            .ToList();

        var windows = EnumerateCandidateWindows(excludedWindowHandle);
        var appliedCount = Math.Min(orderedRegions.Count, windows.Count);

        for (var index = 0; index < appliedCount; index++)
        {
            var region = orderedRegions[index];
            SnapWindowToBounds(windows[index].Handle, region.Bounds);
        }

        return new WorkspaceApplyResult(
            orderedRegions.Count,
            windows.Count,
            appliedCount,
            Math.Max(0, orderedRegions.Count - appliedCount),
            Math.Max(0, windows.Count - appliedCount));
    }

    public void SnapWindowToBounds(IntPtr windowHandle, PaneRect bounds)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        TryDisableRoundedCorners(windowHandle);
        var seamCoveredBounds = ExpandBoundsForSeamCover(bounds);
        var targetBounds = AdjustWindowBoundsForFrame(windowHandle, seamCoveredBounds);

        SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            (int)Math.Round(targetBounds.X),
            (int)Math.Round(targetBounds.Y),
            Math.Max(0, (int)Math.Round(targetBounds.Width)),
            Math.Max(0, (int)Math.Round(targetBounds.Height)),
            SwpNoZOrder | SwpNoActivate);
    }

    public void RestoreWindowToBounds(IntPtr windowHandle, PaneRect bounds)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        RestoreRoundedCorners(windowHandle);
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

        var targetBounds = ApplyWindowFrameAdjustment(ExpandBoundsForSeamCover(bounds), frameAdjustment);

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

        var deferHandle = BeginDeferWindowPos(updates.Count);
        if (deferHandle != IntPtr.Zero)
        {
            foreach (var update in updates)
            {
                if (update.WindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                var targetBounds = ApplyWindowFrameAdjustment(
                    ExpandBoundsForSeamCover(update.Bounds),
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

    public WindowFrameAdjustment GetWindowFrameAdjustment(IntPtr windowHandle)
    {
        return TryGetWindowFrameAdjustment(windowHandle, out var frameAdjustment)
            ? frameAdjustment
            : default;
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

    private static bool TryGetCursorPosition(out NativePoint point)
    {
        return GetCursorPos(out point);
    }

    private static IntPtr MakePointLParam(int x, int y)
    {
        return (IntPtr)((y << 16) | (x & 0xFFFF));
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
        if (windowHandle == IntPtr.Zero)
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

    private static List<WindowCandidate> EnumerateCandidateWindows(IntPtr excludedWindowHandle)
    {
        var currentProcessId = Environment.ProcessId;
        var shellWindow = GetShellWindow();
        var windows = new List<WindowCandidate>();

        var contextHandle = GCHandle.Alloc(new EnumerationContext(windows, excludedWindowHandle, shellWindow, currentProcessId));
        try
        {
            EnumWindows(
                static (handle, parameter) =>
                {
                    var context = (EnumerationContext)GCHandle.FromIntPtr(parameter).Target!;

                    if (handle == IntPtr.Zero
                        || handle == context.ExcludedWindowHandle
                        || handle == context.ShellWindow
                        || !IsWindowVisible(handle)
                        || IsIconic(handle)
                        || GetWindow(handle, GetWindowCommand.Owner) != IntPtr.Zero
                        || IsToolWindow(handle)
                        || IsCloaked(handle))
                    {
                        return true;
                    }

                    GetWindowThreadProcessId(handle, out var processId);
                    if (processId == context.CurrentProcessId)
                    {
                        return true;
                    }

                    var title = GetWindowTitle(handle);
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        return true;
                    }

                    context.Windows.Add(new WindowCandidate(handle, title));
                    return true;
                },
                GCHandle.ToIntPtr(contextHandle));
        }
        finally
        {
            if (contextHandle.IsAllocated)
            {
                contextHandle.Free();
            }
        }

        return windows;
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool IsToolWindow(IntPtr handle)
    {
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        return (style & WsExToolWindow) == WsExToolWindow;
    }

    private static bool IsCloaked(IntPtr handle)
    {
        return DwmGetWindowAttribute(handle, DwmaCloaked, out var cloaked, Marshal.SizeOf<int>()) == 0
            && cloaked != 0;
    }

    private static bool TryGetExtendedFrameBounds(IntPtr handle, out NativeRect frameRect)
    {
        return DwmGetExtendedFrameBounds(handle, DwmaExtendedFrameBounds, out frameRect, Marshal.SizeOf<NativeRect>()) == 0;
    }

    private static void TryDisableRoundedCorners(IntPtr handle)
    {
        try
        {
            var preference = DwmwcpDoNotRound;
            _ = DwmSetWindowAttribute(handle, DwmaWindowCornerPreference, ref preference, Marshal.SizeOf<int>());
        }
        catch
        {
        }
    }

    private sealed record WindowCandidate(IntPtr Handle, string Title);

    private sealed record EnumerationContext(
        List<WindowCandidate> Windows,
        IntPtr ExcludedWindowHandle,
        IntPtr ShellWindow,
        int CurrentProcessId);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private enum GetWindowCommand : uint
    {
        Owner = 4
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, GetWindowCommand uCmd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetExtendedFrameBounds(IntPtr hwnd, int dwAttribute, out NativeRect pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginDeferWindowPos(int nNumWindows);

    [DllImport("user32.dll")]
    private static extern IntPtr DeferWindowPos(
        IntPtr hWinPosInfo,
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool EndDeferWindowPos(IntPtr hWinPosInfo);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, nuint wParam, IntPtr lParam);
}
