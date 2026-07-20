using System.Collections.Concurrent;
using System.Diagnostics;
using PaneWorks.Core.Models;

namespace PaneWorks.Infrastructure.Windows;

public sealed partial class WorkspaceApplyService
{
    private static readonly string[] SnapSeamUnsafeProcessNameTokens =
    [
        "toolbag",
        "marmoset"
    ];
    private static readonly ConcurrentDictionary<IntPtr, WindowCompatibilityCacheEntry> WindowCompatibilityCache = new();
    private static readonly TimeSpan WindowCompatibilityCacheDuration = TimeSpan.FromSeconds(3);

    public bool UsesConservativeSnapHandling(IntPtr windowHandle)
    {
        return UsesConservativeSnapMode(windowHandle);
    }

    private PaneRect GetSnapTargetBounds(IntPtr windowHandle, PaneRect bounds)
    {
        return UsesConservativeSnapMode(windowHandle)
            ? bounds
            : ExpandBoundsForSeamCover(bounds);
    }

    private PaneRect GetSnappedWindowBounds(IntPtr windowHandle, PaneRect bounds)
    {
        if (UsesConservativeSnapMode(windowHandle))
        {
            return bounds;
        }

        TryDisableRoundedCorners(windowHandle);
        return AdjustWindowBoundsForFrame(windowHandle, GetSnapTargetBounds(windowHandle, bounds));
    }

    private PaneRect GetSnappedWindowBounds(
        IntPtr windowHandle,
        PaneRect bounds,
        WindowFrameAdjustment frameAdjustment)
    {
        return UsesConservativeSnapMode(windowHandle)
            ? bounds
            : ApplyWindowFrameAdjustment(GetSnapTargetBounds(windowHandle, bounds), frameAdjustment);
    }

    private PaneRect GetRestoreWindowBounds(IntPtr windowHandle, PaneRect bounds)
    {
        return UsesConservativeSnapMode(windowHandle)
            ? bounds
            : AdjustWindowBoundsForFrame(windowHandle, bounds);
    }

    private static bool UsesConservativeSnapMode(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == 0)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (WindowCompatibilityCache.TryGetValue(windowHandle, out var cached)
            && cached.ProcessId == processId
            && now - cached.CheckedAt <= WindowCompatibilityCacheDuration)
        {
            return cached.UsesConservativeHandling;
        }

        var usesConservativeHandling = TryGetWindowProcessIdentity(windowHandle, out var processName, out var executablePath)
            && MatchesSnapSeamUnsafeProcess(processName, executablePath);
        WindowCompatibilityCache[windowHandle] = new WindowCompatibilityCacheEntry(
            processId,
            usesConservativeHandling,
            now);
        return usesConservativeHandling;
    }

    private readonly record struct WindowCompatibilityCacheEntry(
        uint ProcessId,
        bool UsesConservativeHandling,
        DateTimeOffset CheckedAt);

    private static bool TryGetWindowProcessIdentity(IntPtr windowHandle, out string processName, out string executablePath)
    {
        processName = string.Empty;
        executablePath = string.Empty;

        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName ?? string.Empty;

            try
            {
                executablePath = process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                executablePath = string.Empty;
            }

            return !string.IsNullOrWhiteSpace(processName) || !string.IsNullOrWhiteSpace(executablePath);
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesSnapSeamUnsafeProcess(string processName, string executablePath)
    {
        foreach (var token in SnapSeamUnsafeProcessNameTokens)
        {
            if ((!string.IsNullOrWhiteSpace(processName)
                 && processName.Contains(token, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(executablePath)
                    && executablePath.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
