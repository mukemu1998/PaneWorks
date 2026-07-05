using System.Diagnostics;
using System.IO;
using PaneWorks.Core.Models;

namespace PaneWorks.App;

public partial class MainWindow
{
    private static bool TryCreatePackagedAppAliasLaunchInfo(
        WorkspaceWindowBinding binding,
        out ProcessStartInfo startInfo)
    {
        startInfo = default!;

        if (!IsLaunchablePackagedAppBinding(binding))
        {
            return false;
        }

        if (TryGetSystemNotepadPath(binding, out var notepadPath))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = notepadPath,
                Arguments = ResolveWorkspaceLaunchArguments(binding),
                WorkingDirectory = Path.GetDirectoryName(notepadPath) ?? string.Empty,
                UseShellExecute = true
            };
            return true;
        }

        startInfo = new ProcessStartInfo
        {
            FileName = GetProcessExecutableAlias(binding.ProcessName),
            Arguments = ResolveWorkspaceLaunchArguments(binding),
            UseShellExecute = true
        };
        return true;
    }

    private static bool IsLaunchablePackagedAppBinding(WorkspaceWindowBinding binding)
    {
        return !string.IsNullOrWhiteSpace(binding.ProcessName)
            && IsWindowsAppsExecutablePath(binding.ExecutablePath);
    }

    private static bool TryGetSystemNotepadPath(
        WorkspaceWindowBinding binding,
        out string notepadPath)
    {
        notepadPath = string.Empty;
        if (!IsNotepadBinding(binding))
        {
            return false;
        }

        var systemNotepadPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "notepad.exe");
        if (!File.Exists(systemNotepadPath))
        {
            return false;
        }

        notepadPath = systemNotepadPath;
        return true;
    }

    private static bool IsNotepadBinding(WorkspaceWindowBinding binding)
    {
        return string.Equals(binding.ProcessName, "Notepad", StringComparison.OrdinalIgnoreCase)
            || string.Equals(binding.ProcessName, "Notepad.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProcessExecutableAlias(string processName)
    {
        var normalizedProcessName = processName.Trim();
        return normalizedProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalizedProcessName
            : $"{normalizedProcessName}.exe";
    }

    private static bool IsWindowsAppsExecutablePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            var normalizedPath = Path.GetFullPath(executablePath);
            var windowsAppsPath = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps"));

            return normalizedPath.StartsWith(
                windowsAppsPath + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return executablePath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
        }
    }
}
