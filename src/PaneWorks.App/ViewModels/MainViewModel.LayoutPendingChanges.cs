using System.Threading.Tasks;
using System.Windows;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool ResolvePendingChanges(string actionLabel)
    {
        if (!IsLayoutEditMode || !IsDirty)
        {
            return true;
        }

        var result = ShowMessage(BuildPendingChangesPrompt(actionLabel), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == MessageBoxResult.No)
        {
            return true;
        }

        return SaveToTarget(Slugify(CurrentLayoutName), CurrentLayoutName, treatAsSaveAs: false, notifyOnSuccess: false);
    }

    private async Task<bool> ResolvePendingChangesAsync(string actionLabel, bool discardChangesOnNo = false)
    {
        if (!IsLayoutEditMode || !IsDirty)
        {
            return true;
        }

        var result = ShowMessage(BuildPendingChangesPrompt(actionLabel), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == MessageBoxResult.No)
        {
            if (discardChangesOnNo)
            {
                DiscardPendingLayoutChanges();
            }

            return true;
        }

        return await SaveToTargetAsync(Slugify(CurrentLayoutName), CurrentLayoutName, treatAsSaveAs: false, notifyOnSuccess: false);
    }

    private void DiscardPendingLayoutChanges()
    {
        _currentWorkspaceDocument = NormalizeWorkspaceForCurrentDisplays(_savedState.WorkspaceDocument);
        _currentLayoutId = _savedState.LayoutId;
        CurrentDocument = GetDisplayLayout(_currentWorkspaceDocument, SelectedDisplayItem?.Id);
        SelectedNodeId = CurrentDocument.Root.Id;
        SyncActiveSnapWithCurrentWorkspaceIfNeeded();
        IsDirty = false;
        ResetHistory();
        RaisePropertyChanged(nameof(DisplayedLayoutName));
        SaveSessionState();
        SetStatusMessage("已放弃未保存的分区修改");
    }

    private string BuildPendingChangesPrompt(string actionLabel)
    {
        var changeDescription = string.IsNullOrWhiteSpace(_pendingLayoutChangeDescription)
            ? "分区布局已被修改"
            : _pendingLayoutChangeDescription;

        return $"分区布局“{CurrentLayoutName}”尚未保存。\n{changeDescription}\n是否先保存再{actionLabel}？";
    }
}
