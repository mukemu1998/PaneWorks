using System.Windows;
using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool SaveToTarget(string targetId, string targetName, bool treatAsSaveAs, bool notifyOnSuccess)
    {
        var previousId = _currentLayoutId;
        var tracksCurrentAsSnap = ShouldSyncActiveSnapWithCurrentWorkspace();
        var willRenameCurrentFile = !treatAsSaveAs
            && !string.IsNullOrWhiteSpace(previousId)
            && !string.Equals(previousId, targetId, StringComparison.OrdinalIgnoreCase);

        var shouldOverwrite = Layouts.Any(item =>
            string.Equals(item.Id, targetId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Id, previousId, StringComparison.OrdinalIgnoreCase));

        if (shouldOverwrite)
        {
            var overwrite = ShowMessage($"已存在名为“{targetName}”的分区布局，是否覆盖？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (overwrite != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        var workspaceToSave = RenameWorkspace(_currentWorkspaceDocument, targetName);

        try
        {
            PaneWorksLog.Info($"Save layout sync start: {targetId}");
            _layoutRepository
                .SaveAsync(targetId, workspaceToSave, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (willRenameCurrentFile)
            {
                _layoutRepository
                    .DeleteAsync(previousId!, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            PaneWorksLog.Info($"Save layout sync done: {targetId}");
        }
        catch (Exception ex)
        {
            PaneWorksLog.Error($"Save layout sync failed: {targetId}", ex);
            ShowErrorMessage($"保存分区布局失败：{ex.Message}");
            return false;
        }

        _currentWorkspaceDocument = workspaceToSave;
        _currentLayoutId = targetId;
        _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);
        IsDirty = false;
        if (willRenameCurrentFile)
        {
            HandleLayoutIdRenamedForWorkspaceProfiles(previousId, targetId);
        }

        if (tracksCurrentAsSnap)
        {
            SetActiveSnapWorkspace(targetId, targetName, _currentWorkspaceDocument);
        }

        ActivateDisplay(SelectedDisplayItem?.Id ?? GetPrimaryDisplayId(), resetHistory: false);
        SaveSessionState();

        RefreshLayouts();
        RefreshWorkspaceProfiles();
        SelectedLayoutItem = Layouts.FirstOrDefault(item => item.Id == _currentLayoutId);

        if (notifyOnSuccess)
        {
            ShowInfoMessage($"分区布局“{targetName}”已保存。当前所有屏幕的编辑结果都会写进同一个文件。");
        }

        if (!notifyOnSuccess)
        {
            SetStatusMessage($"分区布局“{targetName}”已保存");
        }

        return true;
    }

    private async Task<bool> SaveToTargetAsync(string targetId, string targetName, bool treatAsSaveAs, bool notifyOnSuccess)
    {
        var previousId = _currentLayoutId;
        var tracksCurrentAsSnap = ShouldSyncActiveSnapWithCurrentWorkspace();
        var willRenameCurrentFile = !treatAsSaveAs
            && !string.IsNullOrWhiteSpace(previousId)
            && !string.Equals(previousId, targetId, StringComparison.OrdinalIgnoreCase);

        var shouldOverwrite = Layouts.Any(item =>
            string.Equals(item.Id, targetId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Id, previousId, StringComparison.OrdinalIgnoreCase));

        if (shouldOverwrite)
        {
            var overwrite = ShowMessage($"已存在名为“{targetName}”的分区布局，是否覆盖？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (overwrite != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        var workspaceToSave = RenameWorkspace(_currentWorkspaceDocument, targetName);

        try
        {
            PaneWorksLog.Info($"Save layout async start: {targetId}");
            await _layoutRepository
                .SaveAsync(targetId, workspaceToSave, CancellationToken.None);

            if (willRenameCurrentFile)
            {
                await _layoutRepository
                    .DeleteAsync(previousId!, CancellationToken.None);
            }
            PaneWorksLog.Info($"Save layout async done: {targetId}");
        }
        catch (Exception ex)
        {
            PaneWorksLog.Error($"Save layout async failed: {targetId}", ex);
            ShowErrorMessage($"保存分区布局失败：{ex.Message}");
            return false;
        }

        _currentWorkspaceDocument = workspaceToSave;
        _currentLayoutId = targetId;
        _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);
        IsDirty = false;
        if (willRenameCurrentFile)
        {
            HandleLayoutIdRenamedForWorkspaceProfiles(previousId, targetId);
        }

        if (tracksCurrentAsSnap)
        {
            SetActiveSnapWorkspace(targetId, targetName, _currentWorkspaceDocument);
        }

        ActivateDisplay(SelectedDisplayItem?.Id ?? GetPrimaryDisplayId(), resetHistory: false);
        SaveSessionState();

        await RefreshLayoutsAsync();
        await RefreshWorkspaceProfilesAsync();
        SelectedLayoutItem = Layouts.FirstOrDefault(item => item.Id == _currentLayoutId);

        if (notifyOnSuccess)
        {
            ShowInfoMessage($"分区布局“{targetName}”已保存。当前所有屏幕的编辑结果都会写进同一个文件。");
        }

        if (!notifyOnSuccess)
        {
            SetStatusMessage($"分区布局“{targetName}”已保存");
        }

        return true;
    }
}
