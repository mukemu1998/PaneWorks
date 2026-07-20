using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private async void CreateNewLayout()
    {
        if (!await ResolvePendingChangesAsync("新建分区布局"))
        {
            return;
        }

        _currentWorkspaceDocument = CreateWorkspaceDocument(DefaultBlankWorkspaceName);
        _currentLayoutId = null;
        _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);
        IsDirty = false;
        EnterLayoutEditMode("已进入新建分区编辑模式。");
        ResetHistory();
        SyncActiveSnapWithCurrentWorkspaceIfNeeded();
        ActivateDisplay(SelectedDisplayItem?.Id ?? GetPrimaryDisplayId(), resetHistory: false);
        SaveSessionState();
    }

    private async void LoadSelectedLayout()
    {
        if (IsLayoutEditMode || SelectedLayoutItem is null)
        {
            return;
        }

        if (!await ResolvePendingChangesAsync("打开选中的分区布局"))
        {
            return;
        }

        try
        {
            var workspace = NormalizeWorkspaceForCurrentDisplays(
                await _layoutRepository.LoadAsync(SelectedLayoutItem.Id, CancellationToken.None));

            _currentWorkspaceDocument = workspace;
            _currentLayoutId = SelectedLayoutItem.Id;
            _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);
            IsDirty = false;
            EnterLayoutEditMode($"正在编辑分区布局“{workspace.Name}”。");
            ResetHistory();
            SyncActiveSnapWithCurrentWorkspaceIfNeeded();
            ActivateDisplay(SelectedDisplayItem?.Id ?? GetPrimaryDisplayId(), resetHistory: false);
            SaveSessionState();
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"打开分区布局失败：{ex.Message}");
        }
    }

    private async void EditActiveSnapLayout()
    {
        if (!await ResolvePendingChangesAsync("编辑当前吸附选择"))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_activeSnapLayoutId))
        {
            try
            {
                _currentWorkspaceDocument = NormalizeWorkspaceForCurrentDisplays(
                    await _layoutRepository.LoadAsync(_activeSnapLayoutId, CancellationToken.None));
                _currentLayoutId = _activeSnapLayoutId;
            }
            catch (Exception ex)
            {
                ShowErrorMessage($"打开当前吸附分区失败：{ex.Message}");
                return;
            }
        }
        else
        {
            _currentWorkspaceDocument = NormalizeWorkspaceForCurrentDisplays(_activeSnapWorkspaceDocument);
            _currentLayoutId = null;
        }

        _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);
        IsDirty = false;
        EnterLayoutEditMode($"正在编辑当前吸附选择“{_currentWorkspaceDocument.Name}”。");
        ResetHistory();
        ActivateDisplay(SelectedDisplayItem?.Id ?? GetPrimaryDisplayId(), resetHistory: false);
        SaveSessionState();
    }

    private async void ExitLayoutEditMode()
    {
        if (!await ResolvePendingChangesAsync("退出分区编辑", discardChangesOnNo: true))
        {
            return;
        }

        LeaveLayoutEditMode("已退出分区编辑模式");
    }

    private void LeaveLayoutEditMode(string statusMessage)
    {
        IsLayoutEditMode = false;
        SelectedLayoutItem = null;
        SelectedNodeId = null;
        SetStatusMessage(statusMessage);
    }

    private void EnterLayoutEditMode(string statusMessage)
    {
        if (IsWorkspaceBindingMode)
        {
            IsWorkspaceBindingMode = false;
        }

        IsLayoutEditMode = true;
        SetStatusMessage(statusMessage);
    }
}
