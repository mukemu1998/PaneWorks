using System;
using System.Linq;
using System.Threading;
using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    public bool IsCurrentLayoutDrivingSnapLayout => ShouldSyncActiveSnapWithCurrentWorkspace();

    public LayoutDocument GetSnapLayoutDocumentForDisplay(string displayId)
    {
        return GetDisplayLayout(_activeSnapWorkspaceDocument, displayId);
    }

    public LayoutDocument GetCurrentLayoutDocumentForDisplay(string displayId)
    {
        return GetDisplayLayout(_currentWorkspaceDocument, displayId);
    }

    public bool UpdateSnapLayoutSplitRatioForDisplay(string displayId, string splitNodeId, double ratio, bool persist)
    {
        var sourceDocument = GetDisplayLayout(_activeSnapWorkspaceDocument, displayId);
        var result = _editorService.UpdateSplitRatio(sourceDocument, splitNodeId, ratio);
        if (!result.Changed)
        {
            if (persist)
            {
                PersistActiveSnapWorkspaceChange();
            }

            return false;
        }

        _activeSnapWorkspaceDocument = ReplaceDisplayLayout(_activeSnapWorkspaceDocument, displayId, result.Document);

        if (ShouldSyncActiveSnapWithCurrentWorkspace())
        {
            _currentWorkspaceDocument = ReplaceDisplayLayout(_currentWorkspaceDocument, displayId, result.Document);

            if (string.Equals(displayId, SelectedDisplayItem?.Id, StringComparison.OrdinalIgnoreCase))
            {
                CurrentDocument = result.Document;
                UpdateDirtyState();
            }

            if (persist)
            {
                PersistActiveSnapWorkspaceChange();
            }

            return true;
        }

        if (persist)
        {
            PersistActiveSnapWorkspaceChange();
        }

        return true;
    }

    public void SwitchSnapLayout(string layoutId, bool notifyOnSuccess)
    {
        if (string.IsNullOrWhiteSpace(layoutId))
        {
            return;
        }

        TrySetSnapLayout(layoutId, notifyOnSuccess);
    }

    private void PersistActiveSnapWorkspaceChange()
    {
        if (ShouldSyncActiveSnapWithCurrentWorkspace())
        {
            if (string.IsNullOrWhiteSpace(_currentLayoutId))
            {
                UpdateDirtyState();
                return;
            }

            _layoutRepository
                .SaveAsync(_currentLayoutId, _currentWorkspaceDocument, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);
            UpdateDirtyState();
            return;
        }

        if (string.IsNullOrWhiteSpace(_activeSnapLayoutId))
        {
            return;
        }

        _layoutRepository
            .SaveAsync(_activeSnapLayoutId, _activeSnapWorkspaceDocument, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private bool TrySetSnapLayout(string layoutId, bool notifyOnSuccess)
    {
        var selectedItem = Layouts.FirstOrDefault(item =>
            string.Equals(item.Id, layoutId, StringComparison.OrdinalIgnoreCase));

        if (selectedItem is null)
        {
            ShowErrorMessage("未找到要切换的分区布局。");
            return false;
        }

        try
        {
            var workspace = NormalizeWorkspaceForCurrentDisplays(
                _layoutRepository.LoadAsync(selectedItem.Id, CancellationToken.None).GetAwaiter().GetResult());

            SelectedLayoutItem = selectedItem;
            SetActiveSnapWorkspace(selectedItem.Id, workspace.Name, workspace);
            ClearActiveWorkspaceProfileAfterPlainLayoutSwitch();
            SaveSessionState();

            if (notifyOnSuccess)
            {
                ShowInfoMessage($"当前分区布局已切换为“{workspace.Name}”。这套多屏布局会直接用于后续所有屏幕吸附。");
            }

            if (!notifyOnSuccess)
            {
                SetStatusMessage($"当前分区布局已切换为“{workspace.Name}”");
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"设置分区布局失败：{ex.Message}");
            return false;
        }
    }

    private void SetActiveSnapWorkspace(string? layoutId, string layoutName, WorkspaceLayoutDocument workspace)
    {
        _activeSnapLayoutId = layoutId;
        _activeSnapLayoutName = layoutName;
        _activeSnapWorkspaceDocument = NormalizeWorkspaceForCurrentDisplays(workspace);
        RaisePropertyChanged(nameof(ActiveSnapLayoutName));
        RaisePropertyChanged(nameof(ActiveSnapLayoutId));
        RaisePropertyChanged(nameof(DisplayedLayoutName));
        RaisePropertyChanged(nameof(SnapLayoutLabel));
        RaisePropertyChanged(nameof(WorkspaceProfileLabel));
        if (CreateWorkspaceProfileFromCurrentLayoutCommand is RelayCommand createWorkspaceFromActiveCommand)
        {
            createWorkspaceFromActiveCommand.RaiseCanExecuteChanged();
        }

        RaiseWindowBindingStatusChanged();
    }

    private bool ShouldSyncActiveSnapWithCurrentWorkspace()
    {
        return string.IsNullOrWhiteSpace(_activeSnapLayoutId)
            || string.Equals(_activeSnapLayoutId, _currentLayoutId, StringComparison.OrdinalIgnoreCase);
    }

    private void SyncActiveSnapWithCurrentWorkspaceIfNeeded()
    {
        if (!ShouldSyncActiveSnapWithCurrentWorkspace())
        {
            return;
        }

        SetActiveSnapWorkspace(_currentLayoutId, _currentWorkspaceDocument.Name, _currentWorkspaceDocument);
    }
}
