using System.Diagnostics;
using System.IO;
using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;

namespace PaneWorks.App;

public partial class MainWindow
{
    private int LaunchMissingWorkspaceWindows(IReadOnlyList<WorkspaceWindowBinding> bindings)
    {
        var launchedCount = 0;
        foreach (var binding in bindings)
        {
            if (!TryCreateWorkspaceLaunchInfo(binding, out var startInfo))
            {
                continue;
            }

            try
            {
                PaneWorksLog.Info($"Launch workspace binding: {binding.ProcessName}, file={startInfo.FileName}, args={startInfo.Arguments}");
                Process.Start(startInfo);
                launchedCount++;
            }
            catch (Exception exception)
            {
                PaneWorksLog.Error($"Launch workspace binding failed: {binding.ProcessName}", exception);
            }
        }

        return launchedCount;
    }

    private static bool TryCreateWorkspaceLaunchInfo(
        WorkspaceWindowBinding binding,
        out ProcessStartInfo startInfo)
    {
        startInfo = default!;

        if (IsExplorerFolderBinding(binding)
            && !string.IsNullOrWhiteSpace(binding.LaunchTarget)
            && Directory.Exists(binding.LaunchTarget))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = QuoteProcessArgument(binding.LaunchTarget),
                WorkingDirectory = binding.LaunchTarget,
                UseShellExecute = true
            };
            return true;
        }

        if (TryCreatePackagedAppAliasLaunchInfo(binding, out startInfo))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(binding.ExecutablePath) && File.Exists(binding.ExecutablePath))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = binding.ExecutablePath,
                Arguments = ResolveWorkspaceLaunchArguments(binding),
                WorkingDirectory = ResolveWorkspaceWorkingDirectory(binding),
                UseShellExecute = true
            };
            return true;
        }

        if (!string.IsNullOrWhiteSpace(binding.LaunchTarget))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = binding.LaunchTarget,
                UseShellExecute = true
            };
            return true;
        }

        return false;
    }

    private static bool CanLaunchWorkspaceBinding(WorkspaceWindowBinding binding)
    {
        if (IsExplorerFolderBinding(binding)
            && !string.IsNullOrWhiteSpace(binding.LaunchTarget)
            && Directory.Exists(binding.LaunchTarget))
        {
            return true;
        }

        return IsLaunchablePackagedAppBinding(binding)
            || (!string.IsNullOrWhiteSpace(binding.ExecutablePath) && File.Exists(binding.ExecutablePath))
            || !string.IsNullOrWhiteSpace(binding.LaunchTarget);
    }

    private static string ResolveWorkspaceLaunchArguments(WorkspaceWindowBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.LaunchArguments))
        {
            return binding.LaunchArguments;
        }

        return string.IsNullOrWhiteSpace(binding.LaunchTarget) ? string.Empty : binding.LaunchTarget;
    }

    private static string ResolveWorkspaceWorkingDirectory(WorkspaceWindowBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.WorkingDirectory) && Directory.Exists(binding.WorkingDirectory))
        {
            return binding.WorkingDirectory;
        }

        return TryGetWorkingDirectory(binding.ExecutablePath);
    }

    private static string QuoteProcessArgument(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
