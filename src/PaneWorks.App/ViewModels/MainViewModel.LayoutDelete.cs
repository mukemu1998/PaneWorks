using System.Windows;
using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private void DeleteSelectedLayout()
    {
        if (SelectedLayoutItem is null)
        {
            return;
        }

        var deletedLayoutId = SelectedLayoutItem.Id;
        var confirmed = ShowMessage($"确定删除分区布局“{SelectedLayoutItem.Name}”吗？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _layoutRepository
                .DeleteAsync(deletedLayoutId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (string.Equals(_currentLayoutId, deletedLayoutId, StringComparison.OrdinalIgnoreCase))
            {
                _currentWorkspaceDocument = CreateWorkspaceDocument(DefaultBlankWorkspaceName);
                _currentLayoutId = null;
                _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);
                IsDirty = false;
                ResetHistory();
                ActivateDisplay(SelectedDisplayItem?.Id ?? GetPrimaryDisplayId(), resetHistory: false);
            }

            if (string.Equals(_activeSnapLayoutId, deletedLayoutId, StringComparison.OrdinalIgnoreCase))
            {
                SetActiveSnapWorkspace(_currentLayoutId, _currentWorkspaceDocument.Name, _currentWorkspaceDocument);
            }

            RefreshLayouts();
            HandleLayoutDeletedForWorkspaceProfiles(deletedLayoutId);
            RefreshWorkspaceProfiles();
            SaveSessionState();
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"删除分区布局失败：{ex.Message}");
        }
    }
}
