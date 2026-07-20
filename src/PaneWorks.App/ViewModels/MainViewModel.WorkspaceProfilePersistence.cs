using System.Windows;
using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task SaveWorkspaceProfileToTargetAsync(
        string targetId,
        WorkspaceProfileDocument profile,
        string? previousId,
        bool notifyOnSuccess)
    {
        var shouldOverwrite = WorkspaceProfiles.Any(item =>
            string.Equals(item.Id, targetId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Id, previousId, StringComparison.OrdinalIgnoreCase));

        if (shouldOverwrite)
        {
            var overwrite = ShowMessage($"已存在名为“{profile.Name}”的工作区，是否覆盖？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (overwrite != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var normalizedProfile = NormalizeWorkspaceProfile(profile);
        if (string.IsNullOrWhiteSpace(normalizedProfile.LayoutId))
        {
            ShowErrorMessage("工作区还没有绑定分区布局，请先从分区布局创建工作区。");
            return;
        }

        try
        {
            await _workspaceProfileRepository.SaveAsync(targetId, normalizedProfile, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"保存工作区失败：{ex.Message}");
            return;
        }

        SetActiveWorkspaceProfile(targetId, normalizedProfile.Name, normalizedProfile);
        SaveSessionState();

        await RefreshWorkspaceProfilesAsync();
        SelectedWorkspaceProfileItem = WorkspaceProfiles.FirstOrDefault(item =>
            string.Equals(item.Id, targetId, StringComparison.OrdinalIgnoreCase));

        if (notifyOnSuccess)
        {
            ShowInfoMessage($"工作区“{normalizedProfile.Name}”已保存。");
        }
        else
        {
            SetStatusMessage($"工作区“{normalizedProfile.Name}”已保存");
        }
    }

    private void DeleteSelectedWorkspaceProfile()
    {
        if (IsWorkspaceBindingMode)
        {
            ShowErrorMessage("请先退出“编辑绑定”，再删除工作区方案。");
            return;
        }

        if (SelectedWorkspaceProfileItem is null)
        {
            return;
        }

        var deletedProfileId = SelectedWorkspaceProfileItem.Id;
        var confirmed = ShowMessage($"确定删除工作区“{SelectedWorkspaceProfileItem.Name}”吗？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _workspaceProfileRepository
                .DeleteAsync(deletedProfileId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (string.Equals(_activeWorkspaceProfileId, deletedProfileId, StringComparison.OrdinalIgnoreCase))
            {
                SetActiveWorkspaceProfile(null, "未启用工作区方案", null);
            }

            RefreshWorkspaceProfiles();
            SaveSessionState();
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"删除工作区失败：{ex.Message}");
        }
    }

    private bool TryBuildWorkspaceProfileDraft(out WorkspaceProfileDocument profileDraft, out string message)
    {
        message = string.Empty;

        if (IsWorkspaceProfileEnabled && _activeWorkspaceProfileDocument is not null)
        {
            profileDraft = NormalizeWorkspaceProfile(_activeWorkspaceProfileDocument);
            return true;
        }

        if (!TryResolveWorkspaceProfileLayoutId(out var layoutId, out var layoutName, out message))
        {
            profileDraft = default!;
            return false;
        }

        profileDraft = new WorkspaceProfileDocument(
            2,
            $"{layoutName} 工作区方案",
            layoutId,
            new List<WorkspaceWindowBinding>());

        return true;
    }

    private bool TryResolveWorkspaceProfileLayoutId(out string layoutId, out string layoutName, out string message)
    {
        layoutId = string.Empty;
        layoutName = string.Empty;
        message = string.Empty;

        if (!string.IsNullOrWhiteSpace(_currentLayoutId))
        {
            layoutId = _currentLayoutId;
            layoutName = _currentWorkspaceDocument.Name;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_activeSnapLayoutId))
        {
            layoutId = _activeSnapLayoutId;
            layoutName = _activeSnapLayoutName;
            return true;
        }

        message = "请先把分区布局保存成文件，再创建工作区方案。";
        return false;
    }
}
