using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PaneWorks.Core.Models;

namespace PaneWorks.Infrastructure.Windows;

public readonly record struct WindowFrameAdjustment(double LeftInset, double TopInset, double RightInset, double BottomInset);

public readonly record struct WindowMinimumSize(double Width, double Height);

public readonly record struct WindowBoundsUpdate(IntPtr WindowHandle, PaneRect Bounds, WindowFrameAdjustment FrameAdjustment);

public sealed class WorkspaceApplyService
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

        TryDisableRoundedCorners(windowHandle);
        var seamCoveredBounds = ExpandBoundsForSeamCover(bounds);
        var targetBounds = AdjustWindowBoundsForFrame(windowHandle, seamCoveredBounds);

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

    private static List<WindowCandidate> EnumerateCandidateWindows(
        IntPtr excludedWindowHandle,
        bool includeExplorerFolderPath = true)
    {
        var currentProcessId = Environment.ProcessId;
        var shellWindow = GetShellWindow();
        var windows = new List<WindowCandidate>();

        var contextHandle = GCHandle.Alloc(new EnumerationContext(
            windows,
            excludedWindowHandle,
            shellWindow,
            currentProcessId,
            includeExplorerFolderPath
                ? GetExplorerFolderPathsByHandle()
                : new Dictionary<IntPtr, string>()));
        try
        {
            EnumWindows(
                static (handle, parameter) =>
                {
                    var context = (EnumerationContext)GCHandle.FromIntPtr(parameter).Target!;
                    if (TryCreateWindowCandidate(handle, context, out var candidate))
                    {
                        context.Windows.Add(candidate);
                    }

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

    private static bool TryCreateWindowCandidate(IntPtr handle, EnumerationContext context, out WindowCandidate candidate)
    {
        candidate = default!;

        if (handle == IntPtr.Zero
            || handle == context.ExcludedWindowHandle
            || handle == context.ShellWindow
            || !IsWindowVisible(handle)
            || IsIconic(handle)
            || GetWindow(handle, GetWindowCommand.Owner) != IntPtr.Zero
            || IsToolWindow(handle)
            || IsCloaked(handle))
        {
            return false;
        }

        GetWindowThreadProcessId(handle, out var processId);
        if (processId == context.CurrentProcessId)
        {
            return false;
        }

        var title = GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (!TryGetProcessInfo(processId, out var processName, out var executablePath)
            || string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var explorerFolderPath = context.ExplorerFolderPathsByHandle.TryGetValue(handle, out var folderPath)
            ? folderPath
            : string.Empty;

        candidate = new WindowCandidate(handle, processName, title, executablePath, explorerFolderPath);
        return true;
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

    private static bool TryGetProcessInfo(uint processId, out string processName, out string executablePath)
    {
        processName = string.Empty;
        executablePath = string.Empty;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;

            try
            {
                executablePath = process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                executablePath = string.Empty;
            }

            return !string.IsNullOrWhiteSpace(processName);
        }
        catch
        {
            return false;
        }
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

    private static IReadOnlyDictionary<IntPtr, string> GetExplorerFolderPathsByHandle()
    {
        IReadOnlyDictionary<IntPtr, string> pathsByHandle = new Dictionary<IntPtr, string>();
        var completed = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                pathsByHandle = GetExplorerFolderPathsByHandleCore();
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "PaneWorks Explorer Folder Lookup"
        };

        try
        {
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completed.Wait(ExplorerFolderLookupTimeout)
                ? pathsByHandle
                : new Dictionary<IntPtr, string>();
        }
        catch
        {
            return new Dictionary<IntPtr, string>();
        }
    }

    private static IReadOnlyDictionary<IntPtr, string> GetExplorerFolderPathsByHandleCore()
    {
        var pathsByHandle = new Dictionary<IntPtr, string>();

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return pathsByHandle;
            }

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return pathsByHandle;
            }

            try
            {
                foreach (var window in shell.Windows())
                {
                    try
                    {
                        var handle = new IntPtr(Convert.ToInt64(window.HWND));
                        var locationUrl = Convert.ToString(window.LocationURL) ?? string.Empty;
                        if (TryConvertExplorerLocationUrlToPath(locationUrl, out string folderPath))
                        {
                            pathsByHandle[handle] = folderPath;
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        TryReleaseComObject(window);
                    }
                }
            }
            finally
            {
                TryReleaseComObject(shell);
            }
        }
        catch
        {
        }

        return pathsByHandle;
    }

    private static bool TryConvertExplorerLocationUrlToPath(string locationUrl, out string folderPath)
    {
        folderPath = string.Empty;
        if (string.IsNullOrWhiteSpace(locationUrl))
        {
            return false;
        }

        try
        {
            var uri = new Uri(locationUrl);
            if (!uri.IsFile)
            {
                return false;
            }

            var localPath = Uri.UnescapeDataString(uri.LocalPath);
            if (!Directory.Exists(localPath))
            {
                return false;
            }

            folderPath = TrimTrailingDirectorySeparators(localPath);
            return !string.IsNullOrWhiteSpace(folderPath);
        }
        catch
        {
            return false;
        }
    }

    private static string TrimTrailingDirectorySeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(root)
            && string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrWhiteSpace(trimmed) ? path : trimmed;
    }

    private static void TryReleaseComObject(object? comObject)
    {
        try
        {
            if (comObject is not null && Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
            }
        }
        catch
        {
        }
    }

    private sealed record WindowCandidate(
        IntPtr Handle,
        string ProcessName,
        string Title,
        string ExecutablePath,
        string ExplorerFolderPath);

    private sealed record EnumerationContext(
        List<WindowCandidate> Windows,
        IntPtr ExcludedWindowHandle,
        IntPtr ShellWindow,
        int CurrentProcessId,
        IReadOnlyDictionary<IntPtr, string> ExplorerFolderPathsByHandle);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, nuint wParam, IntPtr lParam);
}
