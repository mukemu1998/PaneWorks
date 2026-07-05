using System.Threading.Tasks;
using System.Windows;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool ResolvePendingChanges(string actionLabel)
    {
        if (!IsDirty)
        {
            return true;
        }

        var result = ShowMessage($"当前有未保存修改，是否先保存再{actionLabel}？", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
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
        if (!IsDirty)
        {
            return true;
        }

        var result = ShowMessage($"当前有未保存修改，是否先保存再{actionLabel}？", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
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
}
