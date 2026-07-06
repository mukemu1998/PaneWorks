using System.IO;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Persistence;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private string ResolveLayoutDisplayName(string? layoutId)
    {
        if (string.IsNullOrWhiteSpace(layoutId))
        {
            return "未绑定分区布局";
        }

        var item = Layouts.FirstOrDefault(existing =>
            string.Equals(existing.Id, layoutId, StringComparison.OrdinalIgnoreCase));

        return item is null
            ? $"{layoutId}.json（未找到）"
            : item.Name;
    }

    private WorkspaceProfileHealth BuildWorkspaceProfileHealth(WorkspaceProfileListItem item)
    {
        var layoutName = ResolveLayoutDisplayName(item.LayoutId);
        var layoutIssue = ResolveWorkspaceProfileLayoutIssue(item.LayoutId);
        if (item.BindingCount <= 0)
        {
            var suffix = string.IsNullOrWhiteSpace(layoutIssue)
                ? string.Empty
                : $"  |  {layoutIssue}";
            return new WorkspaceProfileHealth($"空绑定  |  关联分区：{layoutName}{suffix}", HasWarning: false);
        }

        try
        {
            var profile = NormalizeWorkspaceProfile(
                _workspaceProfileRepository.LoadAsync(item.Id, CancellationToken.None).GetAwaiter().GetResult());
            var bindings = profile.WindowBindings ?? [];
            var missingExplorerFolders = bindings.Count(binding =>
                IsExplorerFolderBinding(binding)
                && !string.IsNullOrWhiteSpace(binding.LaunchTarget)
                && !Directory.Exists(binding.LaunchTarget));
            var missingExecutablePaths = bindings.Count(binding =>
                !IsExplorerFolderBinding(binding)
                && !string.IsNullOrWhiteSpace(binding.ExecutablePath)
                && !File.Exists(binding.ExecutablePath)
                && !IsPackagedAppBinding(binding));
            var launchlessBindings = bindings.Count(binding =>
                string.IsNullOrWhiteSpace(binding.ExecutablePath)
                && string.IsNullOrWhiteSpace(binding.LaunchTarget));
            var issues = new List<string>();
            if (!string.IsNullOrWhiteSpace(layoutIssue))
            {
                issues.Add(layoutIssue);
            }

            if (missingExplorerFolders > 0)
            {
                issues.Add($"{missingExplorerFolders} 个文件夹路径不存在");
            }

            if (missingExecutablePaths > 0)
            {
                issues.Add($"{missingExecutablePaths} 个程序路径不存在");
            }

            if (launchlessBindings > 0)
            {
                issues.Add($"{launchlessBindings} 个绑定缺少启动信息");
            }

            var prefix = issues.Count == 0
                ? $"可用  |  已绑定 {bindings.Count} 个窗口"
                : $"需检查：{string.Join("，", issues)}";
            return new WorkspaceProfileHealth($"{prefix}  |  关联分区：{layoutName}", issues.Count > 0);
        }
        catch
        {
            return new WorkspaceProfileHealth($"需检查：工作区文件读取失败  |  关联分区：{layoutName}", HasWarning: true);
        }
    }

    private static bool IsPackagedAppBinding(WorkspaceWindowBinding binding)
    {
        return string.Equals(binding.MatchKind, "PackagedApp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(binding.MatchMode, "AppUserModelId", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveWorkspaceProfileLayoutIssue(string? layoutId)
    {
        if (string.IsNullOrWhiteSpace(layoutId))
        {
            return "未绑定分区布局";
        }

        return Layouts.Any(existing =>
            string.Equals(existing.Id, layoutId, StringComparison.OrdinalIgnoreCase))
            ? string.Empty
            : "关联分区文件不存在";
    }

    private sealed record WorkspaceProfileHealth(string Description, bool HasWarning);
}
