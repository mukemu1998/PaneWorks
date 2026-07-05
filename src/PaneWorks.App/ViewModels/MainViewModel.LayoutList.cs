using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private void RefreshLayouts()
    {
        var items = _layoutRepository
            .ListAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ApplyLayoutListItems(items);
    }

    private async Task RefreshLayoutsAsync()
    {
        var items = await _layoutRepository.ListAsync(CancellationToken.None);

        ApplyLayoutListItems(items);
    }

    private void ApplyLayoutListItems(IReadOnlyList<LayoutListItem> items)
    {
        var selectedId = SelectedLayoutItem?.Id ?? _currentLayoutId;

        Layouts.Clear();

        foreach (var item in items)
        {
            Layouts.Add(new LayoutListItemViewModel
            {
                Id = item.Id,
                Name = item.Name,
                Description = $"分区布局文件：{item.Id}.json"
            });
        }

        SelectedLayoutItem = Layouts.FirstOrDefault(item => item.Id == selectedId);
    }

    private void TryRestoreLastLayoutOnStartup()
    {
        var sessionState = _sessionStateRepository.Load();
        SetSelectedDisplayItemSilently(sessionState.SelectedDisplayId ?? GetPrimaryDisplayId());

        if (!string.IsNullOrWhiteSpace(sessionState.LastLayoutId))
        {
            try
            {
                _currentWorkspaceDocument = NormalizeWorkspaceForCurrentDisplays(
                    _layoutRepository.LoadAsync(sessionState.LastLayoutId, CancellationToken.None).GetAwaiter().GetResult());
                _currentLayoutId = sessionState.LastLayoutId;
            }
            catch
            {
                _currentWorkspaceDocument = CreateWorkspaceDocument(DefaultBlankWorkspaceName);
                _currentLayoutId = null;
            }
        }

        _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);
        IsDirty = false;

        if (!string.IsNullOrWhiteSpace(sessionState.LastSnapLayoutId))
        {
            try
            {
                var snapWorkspace = NormalizeWorkspaceForCurrentDisplays(
                    _layoutRepository.LoadAsync(sessionState.LastSnapLayoutId, CancellationToken.None).GetAwaiter().GetResult());
                SetActiveSnapWorkspace(sessionState.LastSnapLayoutId, snapWorkspace.Name, snapWorkspace);
            }
            catch
            {
                SetActiveSnapWorkspace(_currentLayoutId, _currentWorkspaceDocument.Name, _currentWorkspaceDocument);
            }
        }
        else
        {
            SetActiveSnapWorkspace(_currentLayoutId, _currentWorkspaceDocument.Name, _currentWorkspaceDocument);
        }

        ActivateDisplay(SelectedDisplayItem?.Id ?? GetPrimaryDisplayId(), resetHistory: true);
        SelectedLayoutItem = Layouts.FirstOrDefault(item => item.Id == _currentLayoutId);
        TryRestoreWorkspaceProfileSelectionOnStartup(sessionState.LastWorkspaceProfileId);
        SaveSessionState();
    }
}
