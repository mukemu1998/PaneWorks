using PaneWorks.Infrastructure.Persistence;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private void BeginWorkspaceBindingMode()
    {
        if (SelectedWorkspaceProfileItem is null)
        {
            ShowErrorMessage("请先在工作区方案列表里选中一项，再进入绑定模式。");
            return;
        }

        if (!ResolvePendingChanges("进入工作区绑定模式"))
        {
            return;
        }

        try
        {
            var profile = NormalizeWorkspaceProfile(
                _workspaceProfileRepository
                    .LoadAsync(SelectedWorkspaceProfileItem.Id, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            var workspace = NormalizeWorkspaceForCurrentDisplays(
                _layoutRepository
                    .LoadAsync(profile.LayoutId, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

            _currentWorkspaceDocument = workspace;
            _currentLayoutId = profile.LayoutId;
            _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);
            IsDirty = false;
            IsLayoutEditMode = false;
            IsWorkspaceBindingMode = true;
            ResetHistory();
            ActivateDisplay(SelectedDisplayItem?.Id ?? GetPrimaryDisplayId(), resetHistory: false);

            SelectedLayoutItem = Layouts.FirstOrDefault(item =>
                string.Equals(item.Id, profile.LayoutId, StringComparison.OrdinalIgnoreCase));
            SetActiveSnapWorkspace(profile.LayoutId, workspace.Name, workspace);
            SetActiveWorkspaceProfile(SelectedWorkspaceProfileItem.Id, profile.Name, profile);
            SaveSessionState();
            SetStatusMessage($"已进入工作区“{profile.Name}”的区域绑定模式");
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"进入工作区绑定模式失败：{ex.Message}");
        }
    }

    private void ExitWorkspaceBindingMode()
    {
        if (!IsWorkspaceBindingMode)
        {
            return;
        }

        IsWorkspaceBindingMode = false;
        SelectedLayoutItem = null;
        SelectedWorkspaceProfileItem = null;
        SelectedNodeId = null;
        SetStatusMessage("已退出工作区绑定模式");
    }
}
