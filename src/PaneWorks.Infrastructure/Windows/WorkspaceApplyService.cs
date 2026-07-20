using System.Runtime.InteropServices;
using PaneWorks.Core.Models;

namespace PaneWorks.Infrastructure.Windows;

public readonly record struct WindowFrameAdjustment(double LeftInset, double TopInset, double RightInset, double BottomInset);

public readonly record struct WindowMinimumSize(double Width, double Height);

public readonly record struct WindowBoundsUpdate(IntPtr WindowHandle, PaneRect Bounds, WindowFrameAdjustment FrameAdjustment);

public sealed partial class WorkspaceApplyService
{
    private const double SnapSeamOverlap = 1;
    private static readonly TimeSpan ExplorerFolderLookupTimeout = TimeSpan.FromMilliseconds(350);
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const int DwmaCloaked = 14;
    private const int DwmaExtendedFrameBounds = 9;
    private const int DwmaWindowCornerPreference = 33;
    private const int DwmwcpDefault = 0;
    private const int DwmwcpDoNotRound = 1;
    private const int SmCxMinTrack = 34;
    private const int SmCyMinTrack = 35;
    private const uint WmCancelMode = 0x001F;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmNcLButtonDown = 0x00A1;
    private const uint SmtoAbortIfHung = 0x0002;
    private const nuint HtCaption = 0x0002;

    public void SnapWindowToBounds(IntPtr windowHandle, PaneRect bounds)
    {
        _ = TrySnapWindowToBounds(windowHandle, bounds, out _);
    }

    public bool TrySnapWindowToBounds(IntPtr windowHandle, PaneRect bounds, out int errorCode)
    {
        if (windowHandle == IntPtr.Zero)
        {
            errorCode = 0;
            return false;
        }

        var targetBounds = GetSnappedWindowBounds(windowHandle, bounds);

        var moved = SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            (int)Math.Round(targetBounds.X),
            (int)Math.Round(targetBounds.Y),
            Math.Max(0, (int)Math.Round(targetBounds.Width)),
            Math.Max(0, (int)Math.Round(targetBounds.Height)),
            SwpNoZOrder | SwpNoActivate);
        errorCode = moved ? 0 : Marshal.GetLastWin32Error();
        return moved;
    }

    public void RestoreWindowToBounds(IntPtr windowHandle, PaneRect bounds)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        RestoreRoundedCorners(windowHandle);
        var targetBounds = GetRestoreWindowBounds(windowHandle, bounds);

        SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            (int)Math.Round(targetBounds.X),
            (int)Math.Round(targetBounds.Y),
            Math.Max(0, (int)Math.Round(targetBounds.Width)),
            Math.Max(0, (int)Math.Round(targetBounds.Height)),
            SwpNoZOrder | SwpNoActivate);
    }

    public IReadOnlyList<VisibleWindowInfo> GetVisibleWindows(IntPtr excludedWindowHandle)
    {
        return GetVisibleWindows(excludedWindowHandle, includeExplorerFolderPath: true);
    }

    public IReadOnlyList<VisibleWindowInfo> GetVisibleWindows(
        IntPtr excludedWindowHandle,
        bool includeExplorerFolderPath)
    {
        return EnumerateCandidateWindows(excludedWindowHandle, includeExplorerFolderPath)
            .Select(item => new VisibleWindowInfo(
                item.Handle,
                item.ProcessName,
                item.Title,
                item.ExecutablePath,
                item.ExplorerFolderPath))
            .ToList();
    }

    public bool TryGetVisibleWindowInfo(IntPtr windowHandle, IntPtr excludedWindowHandle, out VisibleWindowInfo windowInfo)
    {
        return TryGetVisibleWindowInfo(
            windowHandle,
            excludedWindowHandle,
            includeExplorerFolderPath: true,
            out windowInfo);
    }

    public bool TryGetVisibleWindowInfo(
        IntPtr windowHandle,
        IntPtr excludedWindowHandle,
        bool includeExplorerFolderPath,
        out VisibleWindowInfo windowInfo)
    {
        windowInfo = default!;
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        var currentProcessId = Environment.ProcessId;
        var shellWindow = GetShellWindow();
        var context = new EnumerationContext(
            new List<WindowCandidate>(),
            excludedWindowHandle,
            shellWindow,
            currentProcessId,
            includeExplorerFolderPath
                ? GetExplorerFolderPathsByHandle()
                : new Dictionary<IntPtr, string>());

        if (!TryCreateWindowCandidate(windowHandle, context, out var candidate))
        {
            return false;
        }

        windowInfo = new VisibleWindowInfo(
            candidate.Handle,
            candidate.ProcessName,
            candidate.Title,
            candidate.ExecutablePath,
            candidate.ExplorerFolderPath);
        return true;
    }

    public bool TryGetExplorerFolderPath(IntPtr windowHandle, out string folderPath)
    {
        folderPath = string.Empty;
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        return GetExplorerFolderPathsByHandle().TryGetValue(windowHandle, out folderPath!);
    }

}
