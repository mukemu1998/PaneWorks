using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PaneWorks.Infrastructure.Windows;

public sealed partial class WorkspaceApplyService
{
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
}
