using System.Collections.ObjectModel;
using System.Windows.Input;
using PaneWorks.Core.Abstractions;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Persistence;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly IWorkspaceProfileRepository _workspaceProfileRepository;

    private WorkspaceProfileDocument? _activeWorkspaceProfileDocument;
    private string? _activeWorkspaceProfileId;
    private string _activeWorkspaceProfileName = "未启用工作区方案";
    private LayoutListItemViewModel? _selectedWorkspaceProfileItem;
    private bool _isSavingWorkspaceProfile;
    private RelayCommand _beginWorkspaceBindingModeCommand = null!;
    private RelayCommand _exitWorkspaceBindingModeCommand = null!;

    public ObservableCollection<LayoutListItemViewModel> WorkspaceProfiles { get; }

    public ICommand NewWorkspaceProfileCommand { get; private set; } = null!;

    public ICommand CreateWorkspaceProfileFromCurrentLayoutCommand { get; private set; } = null!;

    public ICommand BeginWorkspaceBindingModeCommand { get; private set; } = null!;

    public ICommand ExitWorkspaceBindingModeCommand { get; private set; } = null!;

    public ICommand SaveWorkspaceProfileCommand { get; private set; } = null!;

    public ICommand SaveAsWorkspaceProfileCommand { get; private set; } = null!;

    public ICommand DeleteSelectedWorkspaceProfileCommand { get; private set; } = null!;

    public LayoutListItemViewModel? SelectedWorkspaceProfileItem
    {
        get => _selectedWorkspaceProfileItem;
        set
        {
            if (!SetProperty(ref _selectedWorkspaceProfileItem, value))
            {
                return;
            }

            _beginWorkspaceBindingModeCommand?.RaiseCanExecuteChanged();
        }
    }

    public string ActiveWorkspaceProfileId => _activeWorkspaceProfileId ?? string.Empty;

    public string ActiveWorkspaceProfileName => _activeWorkspaceProfileName;

    public bool IsWorkspaceProfileEnabled => _activeWorkspaceProfileDocument is not null
        && !string.IsNullOrWhiteSpace(_activeWorkspaceProfileId);

    public IReadOnlyList<WorkspaceWindowBinding> ActiveWorkspaceWindowBindings => GetActiveWorkspaceWindowBindings();

    public string WorkspaceProfileLabel
    {
        get
        {
            if (!IsWorkspaceProfileEnabled || _activeWorkspaceProfileDocument is null)
            {
                return "当前工作区方案：未启用  |  默认不会开机自动启用，需要时手动切换";
            }

            return $"当前工作区方案：{_activeWorkspaceProfileName}  |  关联分区布局：{ResolveLayoutDisplayName(_activeWorkspaceProfileDocument.LayoutId)}";
        }
    }

    private void InitializeWorkspaceProfileCommands()
    {
        NewWorkspaceProfileCommand = new RelayCommand(CreateWorkspaceProfileFromSelectedLayout, () => SelectedLayoutItem is not null);
        CreateWorkspaceProfileFromCurrentLayoutCommand = new RelayCommand(CreateWorkspaceProfileFromActiveSnapLayout, () => !string.IsNullOrWhiteSpace(_activeSnapLayoutId));
        _beginWorkspaceBindingModeCommand = new RelayCommand(BeginWorkspaceBindingMode, () => SelectedWorkspaceProfileItem is not null);
        _exitWorkspaceBindingModeCommand = new RelayCommand(ExitWorkspaceBindingMode, () => IsWorkspaceBindingMode);
        BeginWorkspaceBindingModeCommand = _beginWorkspaceBindingModeCommand;
        ExitWorkspaceBindingModeCommand = _exitWorkspaceBindingModeCommand;
        SaveWorkspaceProfileCommand = new RelayCommand(SaveWorkspaceProfile);
        SaveAsWorkspaceProfileCommand = new RelayCommand(SaveWorkspaceProfileAs);
        DeleteSelectedWorkspaceProfileCommand = new RelayCommand(DeleteSelectedWorkspaceProfile);
    }

    public IReadOnlyList<WorkspaceWindowBinding> GetActiveWorkspaceWindowBindings()
    {
        return NormalizeWindowBindings(_activeWorkspaceProfileDocument?.WindowBindings);
    }

    public bool TrySwitchWorkspaceProfile(string profileId, bool notifyOnSuccess)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        var selectedItem = WorkspaceProfiles.FirstOrDefault(item =>
            string.Equals(item.Id, profileId, StringComparison.OrdinalIgnoreCase));

        if (selectedItem is null)
        {
            ShowErrorMessage("未找到要切换的工作区方案。");
            return false;
        }

        try
        {
            var profile = NormalizeWorkspaceProfile(
                _workspaceProfileRepository.LoadAsync(profileId, CancellationToken.None).GetAwaiter().GetResult());
            if (NormalizeWindowBindings(profile.WindowBindings).Count == 0)
            {
                ShowErrorMessage("这套工作区方案还没有窗口绑定。请先选中它并进入“编辑绑定”模式完成绑定后再应用。");
                return false;
            }

            var workspace = NormalizeWorkspaceForCurrentDisplays(
                _layoutRepository.LoadAsync(profile.LayoutId, CancellationToken.None).GetAwaiter().GetResult());

            SelectedWorkspaceProfileItem = selectedItem;
            SelectedLayoutItem = Layouts.FirstOrDefault(item =>
                string.Equals(item.Id, profile.LayoutId, StringComparison.OrdinalIgnoreCase));
            SetActiveSnapWorkspace(profile.LayoutId, workspace.Name, workspace);
            SetActiveWorkspaceProfile(profileId, profile.Name, profile);
            SaveSessionState();

            if (notifyOnSuccess)
            {
                ShowInfoMessage($"当前工作区方案已切换为“{profile.Name}”。后续用户分区吸附会直接使用它关联的分区布局。");
            }
            else
            {
                SetStatusMessage($"当前工作区方案已切换为“{profile.Name}”");
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"切换工作区方案失败：{ex.Message}");
            return false;
        }
    }

    public void DisableWorkspaceProfile(bool notifyOnSuccess)
    {
        if (!IsWorkspaceProfileEnabled)
        {
            return;
        }

        SetActiveWorkspaceProfile(null, "未启用工作区方案", null);
        IsWorkspaceBindingMode = false;
        SaveSessionState();

        if (notifyOnSuccess)
        {
            ShowInfoMessage("当前工作区方案已停用，后续只保留分区布局切换。");
        }
        else
        {
            SetStatusMessage("当前工作区方案已停用");
        }
    }

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
