using System.Diagnostics;
using System.IO;
using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;

namespace PaneWorks.App;

public partial class MainWindow
{
    private WorkspaceLaunchResult LaunchMissingWorkspaceWindows(IReadOnlyList<WorkspaceWindowBinding> bindings)
    {
        var startedBindings = new List<WorkspaceWindowBinding>();
        var skippedIssues = new List<WorkspaceRestoreIssue>();
        var failedIssues = new List<WorkspaceRestoreIssue>();
        foreach (var binding in bindings)
        {
            if (!TryCreateWorkspaceLaunchInfo(binding, out var startInfo))
            {
                skippedIssues.Add(new WorkspaceRestoreIssue(
                    FormatWorkspaceBindingTarget(binding),
                    ResolveWorkspaceLaunchSkipReason(binding)));
                PaneWorksLog.Info($"Skip launch workspace binding: {binding.ProcessName}, target={binding.LaunchTarget}, file={binding.ExecutablePath}");
                continue;
            }

            try
            {
                PaneWorksLog.Info($"Launch workspace binding: {binding.ProcessName}, file={startInfo.FileName}, args={startInfo.Arguments}");
                Process.Start(startInfo);
                startedBindings.Add(binding);
            }
            catch (Exception exception)
            {
                failedIssues.Add(new WorkspaceRestoreIssue(
                    FormatWorkspaceBindingTarget(binding),
                    $"启动失败：{exception.Message}"));
                PaneWorksLog.Error($"Launch workspace binding failed: {binding.ProcessName}", exception);
            }
        }

        return new WorkspaceLaunchResult(startedBindings, skippedIssues, failedIssues);
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

    private static string ResolveWorkspaceLaunchSkipReason(WorkspaceWindowBinding binding)
    {
        if (IsExplorerFolderBinding(binding))
        {
            return string.IsNullOrWhiteSpace(binding.LaunchTarget)
                ? "缺少文件夹路径，无法自动打开。"
                : $"文件夹路径不存在：{binding.LaunchTarget}";
        }

        if (!string.IsNullOrWhiteSpace(binding.ExecutablePath) && !File.Exists(binding.ExecutablePath))
        {
            return $"程序路径不存在：{binding.ExecutablePath}";
        }

        return "缺少可用的程序路径或启动目标。";
    }
}
