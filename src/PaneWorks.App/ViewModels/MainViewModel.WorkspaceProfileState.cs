using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Persistence;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    public void RefreshWorkspaceProfiles()
    {
        var items = _workspaceProfileRepository
            .ListAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ApplyWorkspaceProfileListItems(items);
    }

    public async Task RefreshWorkspaceProfilesAsync()
    {
        var items = await _workspaceProfileRepository.ListAsync(CancellationToken.None);
        ApplyWorkspaceProfileListItems(items);
    }

    public void TryRestoreWorkspaceProfileSelectionOnStartup(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        SelectedWorkspaceProfileItem = WorkspaceProfiles.FirstOrDefault(item =>
            string.Equals(item.Id, profileId, StringComparison.OrdinalIgnoreCase));
    }

    public void HandleLayoutIdRenamedForWorkspaceProfiles(string? previousLayoutId, string newLayoutId)
    {
        if (string.IsNullOrWhiteSpace(previousLayoutId)
            || string.IsNullOrWhiteSpace(newLayoutId)
            || string.Equals(previousLayoutId, newLayoutId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var items = _workspaceProfileRepository
            .ListAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        foreach (var item in items.Where(item =>
                     string.Equals(item.LayoutId, previousLayoutId, StringComparison.OrdinalIgnoreCase)))
        {
            var profile = NormalizeWorkspaceProfile(
                _workspaceProfileRepository.LoadAsync(item.Id, CancellationToken.None).GetAwaiter().GetResult());
            profile = profile with { LayoutId = newLayoutId };
            _workspaceProfileRepository
                .SaveAsync(item.Id, profile, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (string.Equals(item.Id, _activeWorkspaceProfileId, StringComparison.OrdinalIgnoreCase))
            {
                SetActiveWorkspaceProfile(item.Id, profile.Name, profile);
            }
        }
    }

    public void HandleLayoutDeletedForWorkspaceProfiles(string layoutId)
    {
        if (_activeWorkspaceProfileDocument is null)
        {
            return;
        }

        if (!string.Equals(_activeWorkspaceProfileDocument.LayoutId, layoutId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SetActiveWorkspaceProfile(null, "未启用工作区方案", null);
    }

    private void ApplyWorkspaceProfileListItems(IReadOnlyList<WorkspaceProfileListItem> items)
    {
        var selectedId = SelectedWorkspaceProfileItem?.Id ?? _activeWorkspaceProfileId;

        WorkspaceProfiles.Clear();

        foreach (var item in items)
        {
            var health = BuildWorkspaceProfileHealth(item);
            WorkspaceProfiles.Add(new LayoutListItemViewModel
            {
                Id = item.Id,
                Name = item.Name,
                Description = health.Description,
                IsEmptyWorkspaceBinding = item.BindingCount <= 0,
                HasWorkspaceBinding = item.BindingCount > 0 && !health.HasWarning,
                HasWorkspaceWarning = health.HasWarning
            });
        }

        SelectedWorkspaceProfileItem = WorkspaceProfiles.FirstOrDefault(item =>
            string.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase));

        RaisePropertyChanged(nameof(WorkspaceProfileLabel));
    }

    private bool IsCurrentEditorUsingActiveWorkspaceLayout()
    {
        return _activeWorkspaceProfileDocument is not null
            && !string.IsNullOrWhiteSpace(_currentLayoutId)
            && string.Equals(_currentLayoutId, _activeWorkspaceProfileDocument.LayoutId, StringComparison.OrdinalIgnoreCase);
    }

    private void SetActiveWorkspaceProfile(string? profileId, string profileName, WorkspaceProfileDocument? profile)
    {
        _activeWorkspaceProfileId = profileId;
        _activeWorkspaceProfileName = string.IsNullOrWhiteSpace(profileId) ? "未启用工作区方案" : profileName;
        _activeWorkspaceProfileDocument = profile is null ? null : NormalizeWorkspaceProfile(profile);
        RaisePropertyChanged(nameof(ActiveWorkspaceProfileId));
        RaisePropertyChanged(nameof(ActiveWorkspaceProfileName));
        RaisePropertyChanged(nameof(IsWorkspaceProfileEnabled));
        RaisePropertyChanged(nameof(WorkspaceProfileLabel));
        RaisePropertyChanged(nameof(CanEditWorkspaceBindings));
        RaisePropertyChanged(nameof(ActiveWorkspaceWindowBindings));
        RaiseWindowBindingStatusChanged();
        UpdateWorkspaceBindingCommandStates();
    }

    private void ClearActiveWorkspaceProfileAfterPlainLayoutSwitch()
    {
        if (!IsWorkspaceProfileEnabled)
        {
            return;
        }

        SetActiveWorkspaceProfile(null, "未启用工作区方案", null);
        IsWorkspaceBindingMode = false;
    }

    private void UpdateWorkspaceBindingCommandStates()
    {
        _beginWorkspaceBindingModeCommand?.RaiseCanExecuteChanged();
        _exitWorkspaceBindingModeCommand?.RaiseCanExecuteChanged();
    }

}
