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
    private IReadOnlyList<WorkspaceBindingIconItemViewModel> _selectedWorkspaceProfileWindowBindings = [];
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
            RaisePropertyChanged(nameof(CanEnterWorkspaceBindingMode));
            RaisePropertyChanged(nameof(CanManageSelectedWorkspaceProfile));
            RefreshSelectedWorkspaceProfileBindingPreview();
            UpdateWorkspaceBindingCommandStates();
        }
    }

    public string ActiveWorkspaceProfileId => _activeWorkspaceProfileId ?? string.Empty;

    public string ActiveWorkspaceProfileName => _activeWorkspaceProfileName;

    public bool IsWorkspaceProfileEnabled => _activeWorkspaceProfileDocument is not null
        && !string.IsNullOrWhiteSpace(_activeWorkspaceProfileId);

    public bool IsWorkspaceProfileListSelectionEnabled => !IsWorkspaceBindingMode;

    public bool CanEnterWorkspaceBindingMode => !IsWorkspaceBindingMode
        && SelectedWorkspaceProfileItem is not null;

    public bool CanManageSelectedWorkspaceProfile => !IsWorkspaceBindingMode
        && SelectedWorkspaceProfileItem is not null;

    public bool CanSaveWorkspaceProfileChanges => IsWorkspaceBindingMode
        && IsWorkspaceProfileEnabled;

    public IReadOnlyList<WorkspaceBindingIconItemViewModel> SelectedWorkspaceProfileWindowBindings
        => _selectedWorkspaceProfileWindowBindings;

    public bool HasSelectedWorkspaceProfileWindowBindings
        => _selectedWorkspaceProfileWindowBindings.Count > 0;

    public string SelectedWorkspaceProfileBindingPreviewLabel => SelectedWorkspaceProfileItem is null
        ? "选择左侧工作区方案后，可在这里预览该方案的窗口绑定。"
        : HasSelectedWorkspaceProfileWindowBindings
            ? $"已保存 {SelectedWorkspaceProfileWindowBindings.Count} 个窗口绑定"
            : "当前方案尚未绑定窗口。";

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
        NewWorkspaceProfileCommand = new RelayCommand(CreateWorkspaceProfileFromSelectedLayout, () => !IsWorkspaceBindingMode && SelectedLayoutItem is not null);
        CreateWorkspaceProfileFromCurrentLayoutCommand = new RelayCommand(CreateWorkspaceProfileFromActiveSnapLayout, () => !IsWorkspaceBindingMode && !string.IsNullOrWhiteSpace(_activeSnapLayoutId));
        _beginWorkspaceBindingModeCommand = new RelayCommand(BeginWorkspaceBindingMode, () => CanEnterWorkspaceBindingMode);
        _exitWorkspaceBindingModeCommand = new RelayCommand(ExitWorkspaceBindingMode, () => IsWorkspaceBindingMode);
        BeginWorkspaceBindingModeCommand = _beginWorkspaceBindingModeCommand;
        ExitWorkspaceBindingModeCommand = _exitWorkspaceBindingModeCommand;
        SaveWorkspaceProfileCommand = new RelayCommand(SaveWorkspaceProfile, () => CanSaveWorkspaceProfileChanges);
        SaveAsWorkspaceProfileCommand = new RelayCommand(SaveWorkspaceProfileAs, () => CanSaveWorkspaceProfileChanges);
        DeleteSelectedWorkspaceProfileCommand = new RelayCommand(DeleteSelectedWorkspaceProfile, () => CanManageSelectedWorkspaceProfile);
    }

    public IReadOnlyList<WorkspaceWindowBinding> GetActiveWorkspaceWindowBindings()
    {
        return NormalizeWindowBindings(_activeWorkspaceProfileDocument?.WindowBindings);
    }

    private void RefreshSelectedWorkspaceProfileBindingPreview()
    {
        var profileId = SelectedWorkspaceProfileItem?.Id;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            SetSelectedWorkspaceProfileBindingPreview([]);
            return;
        }

        try
        {
            var profile = string.Equals(profileId, _activeWorkspaceProfileId, StringComparison.OrdinalIgnoreCase)
                && _activeWorkspaceProfileDocument is not null
                ? _activeWorkspaceProfileDocument
                : NormalizeWorkspaceProfile(
                    _workspaceProfileRepository.LoadAsync(profileId, CancellationToken.None).GetAwaiter().GetResult());
            SetSelectedWorkspaceProfileBindingPreview(
                NormalizeWindowBindings(profile.WindowBindings)
                    .OrderBy(binding => binding.DisplayId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(binding => binding.NodeId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(binding => binding.StackOrder)
                    .Select(WorkspaceBindingIconItemViewModel.Create)
                    .ToList());
        }
        catch
        {
            SetSelectedWorkspaceProfileBindingPreview([]);
        }
    }

    private void SetSelectedWorkspaceProfileBindingPreview(IReadOnlyList<WorkspaceBindingIconItemViewModel> bindings)
    {
        _selectedWorkspaceProfileWindowBindings = bindings;
        RaisePropertyChanged(nameof(SelectedWorkspaceProfileWindowBindings));
        RaisePropertyChanged(nameof(HasSelectedWorkspaceProfileWindowBindings));
        RaisePropertyChanged(nameof(SelectedWorkspaceProfileBindingPreviewLabel));
    }

    public bool TrySwitchWorkspaceProfile(string profileId, bool notifyOnSuccess)
    {
        if (IsWorkspaceBindingMode)
        {
            ShowErrorMessage("请先退出“编辑绑定”，再切换并应用其他工作区方案。");
            return false;
        }

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

}
