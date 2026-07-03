using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PaneWorks.Core.Abstractions;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Persistence;
using WpfApplication = System.Windows.Application;

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

    public string BuildSelectedRegionBindingSummary()
    {
        if (!IsWorkspaceProfileEnabled || _activeWorkspaceProfileDocument is null)
        {
            return "当前还没有进入工作区绑定模式。";
        }

        if (!IsWorkspaceBindingMode)
        {
            return "先在左侧选择工作区方案，再点击“编辑绑定”。";
        }

        if (!IsCurrentEditorUsingActiveWorkspaceLayout())
        {
            return $"当前工作区方案“{_activeWorkspaceProfileName}”已启用，但当前编辑的不是它关联的分区布局。";
        }

        if (!TryGetSelectedLeafRegion(out var displayId, out var nodeId))
        {
            return "当前还没有选中可绑定的区域。";
        }

        var regionBindings = GetWindowBindings(_activeWorkspaceProfileDocument, displayId, nodeId);
        var binding = regionBindings.OrderBy(item => item.StackOrder).LastOrDefault();
        if (binding is not null && regionBindings.Count > 1)
        {
            return $"当前区域绑定窗口组：{regionBindings.Count} 个窗口，顶层为 {binding.ProcessName}.exe。";
        }

        if (binding is not null && IsExplorerFolderBinding(binding))
        {
            return $"当前区域绑定文件夹：{binding.LaunchTarget}";
        }

        return binding is null
            ? "当前区域还没有窗口绑定。"
            : string.IsNullOrWhiteSpace(binding.WindowTitleSnapshot)
                ? $"当前区域绑定：{binding.ProcessName}.exe"
                : $"当前区域绑定：{binding.ProcessName}.exe  |  {binding.WindowTitleSnapshot}";
    }

    public string BuildSelectedRegionBindingDescription()
    {
        if (!IsWorkspaceProfileEnabled || _activeWorkspaceProfileDocument is null)
        {
            return "工作区只负责窗口绑定。先从当前吸附或选中的保存分区创建方案，再进入绑定模式。";
        }

        if (!IsWorkspaceBindingMode)
        {
            return "默认不显示桌面绑定覆盖层。点击“编辑绑定”后可选择区域、手动绑定或一键绑定已吸附窗口。";
        }

        if (!IsCurrentEditorUsingActiveWorkspaceLayout())
        {
            return $"请先从左侧分区布局列表里打开“{ResolveLayoutDisplayName(_activeWorkspaceProfileDocument.LayoutId)}”，再编辑这套工作区方案的区域绑定。";
        }

        if (!TryGetSelectedLeafRegion(out var displayId, out var nodeId))
        {
            return "手动绑定需要先点击一个区域；如果窗口已经吸附到各区域，可直接使用一键绑定。";
        }

        var regionBindings = GetWindowBindings(_activeWorkspaceProfileDocument, displayId, nodeId);
        var binding = regionBindings.OrderBy(item => item.StackOrder).LastOrDefault();
        if (binding is null)
        {
            return "当前区域还没有绑定窗口。绑定后，手动切换这套工作区方案时就能按规则恢复窗口。";
        }

        if (regionBindings.Count > 1)
        {
            return $"当前区域是窗口组，共 {regionBindings.Count} 个窗口；恢复工作区时会全部吸附到这里，并让 {binding.ProcessName}.exe 保持在最顶层。";
        }

        if (IsExplorerFolderBinding(binding))
        {
            return $"匹配方式：Explorer 文件夹路径优先。路径：{binding.LaunchTarget}";
        }

        if (!string.IsNullOrWhiteSpace(binding.ExecutablePath))
        {
            return $"匹配方式：优先按程序路径，其次按标题。路径：{binding.ExecutablePath}";
        }

        return string.IsNullOrWhiteSpace(binding.WindowTitleSnapshot)
            ? $"匹配方式：按 {binding.ProcessName}.exe 处理。"
            : $"匹配方式：按 {binding.ProcessName}.exe + 标题快照。标题：{binding.WindowTitleSnapshot}";
    }

    private bool TrySetSelectedRegionWindowBindingCore(
        string processName,
        string windowTitleSnapshot,
        string executablePath,
        string explorerFolderPath,
        out string message)
    {
        message = string.Empty;
        if (!CanEditWorkspaceBindings || _activeWorkspaceProfileDocument is null)
        {
            message = "请先选中工作区方案并点击“编辑绑定”，再给区域绑定窗口。";
            return false;
        }

        if (!IsCurrentEditorUsingActiveWorkspaceLayout())
        {
            message = $"请先打开工作区方案“{_activeWorkspaceProfileName}”关联的分区布局，再编辑区域绑定。";
            return false;
        }

        if (!TryGetSelectedLeafRegion(out var displayId, out var nodeId))
        {
            message = "请先选中一个区域，再给它绑定窗口。";
            return false;
        }

        var normalizedProcessName = processName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedProcessName))
        {
            message = "无效的窗口进程名，无法保存绑定。";
            return false;
        }

        var normalizedExplorerFolderPath = NormalizeFolderPath(explorerFolderPath);
        var isExplorerFolder = IsExplorerProcess(normalizedProcessName)
            && !string.IsNullOrWhiteSpace(normalizedExplorerFolderPath);

        _activeWorkspaceProfileDocument = ReplaceWindowBindingsForRegion(
            _activeWorkspaceProfileDocument,
            new WorkspaceWindowBinding(
                displayId,
                nodeId,
                normalizedProcessName,
                windowTitleSnapshot.Trim(),
                executablePath.Trim(),
                string.Empty,
                isExplorerFolder ? normalizedExplorerFolderPath : string.Empty,
                isExplorerFolder ? "ExplorerFolder" : "Window",
                isExplorerFolder ? "FolderPath" : "Auto",
                isExplorerFolder ? normalizedExplorerFolderPath : string.Empty));

        if (!TrySaveActiveWorkspaceProfileDocument(out var saveMessage))
        {
            message = saveMessage;
            return false;
        }

        RaiseWindowBindingStatusChanged();
        message = isExplorerFolder
            ? $"当前区域已绑定并保存到文件夹：{normalizedExplorerFolderPath}"
            : $"当前区域已绑定并保存到 {normalizedProcessName}.exe。";
        return true;
    }

    private bool TryClearSelectedRegionWindowBindingCore(out string message)
    {
        message = string.Empty;
        if (!CanEditWorkspaceBindings
            || _activeWorkspaceProfileDocument is null
            || string.IsNullOrWhiteSpace(_activeWorkspaceProfileId))
        {
            message = "请先选中工作区方案并点击“编辑绑定”，再清除窗口绑定。";
            SetStatusMessage(message);
            return false;
        }

        if (!IsCurrentEditorUsingActiveWorkspaceLayout())
        {
            message = $"请先打开工作区方案“{_activeWorkspaceProfileName}”关联的分区布局，再编辑区域绑定。";
            SetStatusMessage(message);
            return false;
        }

        if (!TryGetSelectedLeafRegion(out var displayId, out var nodeId))
        {
            message = "请先选中一个区域，再清除绑定。";
            SetStatusMessage(message);
            return false;
        }

        if (GetWindowBinding(_activeWorkspaceProfileDocument, displayId, nodeId) is null)
        {
            message = "当前区域还没有窗口绑定。";
            SetStatusMessage(message);
            return false;
        }

        _activeWorkspaceProfileDocument = NormalizeWorkspaceProfile(
            RemoveWindowBinding(_activeWorkspaceProfileDocument, displayId, nodeId));
        RaiseWindowBindingStatusChanged();
        message = "已清除选中区域绑定，正在后台保存工作区。";
        SetStatusMessage(message);
        SaveWorkspaceProfileDocumentInBackground(
            _activeWorkspaceProfileId,
            _activeWorkspaceProfileDocument,
            "选中区域绑定已清除并后台保存");
        return true;
    }

    public bool TryClearAllWorkspaceWindowBindingsFast(out string message)
    {
        message = string.Empty;
        if (!CanEditWorkspaceBindings
            || _activeWorkspaceProfileDocument is null
            || string.IsNullOrWhiteSpace(_activeWorkspaceProfileId))
        {
            message = "请先选中工作区方案并点击“编辑绑定”，再清除全部绑定。";
            SetStatusMessage(message);
            return false;
        }

        var existingBindings = NormalizeWindowBindings(_activeWorkspaceProfileDocument.WindowBindings);
        if (existingBindings.Count == 0)
        {
            message = "当前工作区还没有窗口绑定。";
            SetStatusMessage(message);
            return false;
        }

        _activeWorkspaceProfileDocument = NormalizeWorkspaceProfile(
            _activeWorkspaceProfileDocument with { WindowBindings = new List<WorkspaceWindowBinding>() });
        RaiseWindowBindingStatusChanged();
        message = $"已清除当前工作区 {existingBindings.Count} 个窗口绑定，正在后台保存。";
        SetStatusMessage(message);
        SaveWorkspaceProfileDocumentInBackground(
            _activeWorkspaceProfileId,
            _activeWorkspaceProfileDocument,
            "当前工作区所有窗口绑定已清除并后台保存");
        return true;
    }

    public bool TryUpsertWorkspaceWindowBindings(IReadOnlyList<WorkspaceWindowBinding> bindings, out string message)
    {
        message = string.Empty;
        if (!CanEditWorkspaceBindings || _activeWorkspaceProfileDocument is null)
        {
            message = "请先选中工作区方案并点击“编辑绑定”，再一键绑定已吸附窗口。";
            return false;
        }

        var normalizedBindings = NormalizeWindowBindings(bindings);
        if (normalizedBindings.Count == 0)
        {
            message = "没有找到可保存的已吸附窗口绑定。";
            return false;
        }

        _activeWorkspaceProfileDocument = ReplaceWindowBindingGroups(_activeWorkspaceProfileDocument, normalizedBindings);
        if (!TrySaveActiveWorkspaceProfileDocument(out var saveMessage))
        {
            message = saveMessage;
            return false;
        }

        RaiseWindowBindingStatusChanged();
        message = $"已一键绑定并保存 {normalizedBindings.Count} 个完全吸附的窗口。";
        return true;
    }

    public bool TryUpsertWorkspaceWindowBindingsFast(IReadOnlyList<WorkspaceWindowBinding> bindings, out string message)
    {
        message = string.Empty;
        if (!CanEditWorkspaceBindings
            || _activeWorkspaceProfileDocument is null
            || string.IsNullOrWhiteSpace(_activeWorkspaceProfileId))
        {
            message = "请先选中工作区方案并点击“编辑绑定”，再一键绑定已吸附窗口。";
            SetStatusMessage(message);
            return false;
        }

        var normalizedBindings = NormalizeWindowBindings(bindings);
        if (normalizedBindings.Count == 0)
        {
            message = "没有找到可保存的已吸附窗口绑定。";
            SetStatusMessage(message);
            return false;
        }

        _activeWorkspaceProfileDocument = NormalizeWorkspaceProfile(
            ReplaceWindowBindingGroups(_activeWorkspaceProfileDocument, normalizedBindings));
        RaiseWindowBindingStatusChanged();

        message = $"已一键绑定 {normalizedBindings.Count} 个窗口，正在后台保存工作区。";
        SetStatusMessage(message);
        SaveWorkspaceProfileDocumentInBackground(
            _activeWorkspaceProfileId,
            _activeWorkspaceProfileDocument,
            $"工作区“{_activeWorkspaceProfileName}”已后台保存");
        return true;
    }

    public bool TryUpsertWorkspaceWindowBindingPatch(WorkspaceWindowBinding binding, string statusMessage)
    {
        if (!IsWorkspaceProfileEnabled
            || _activeWorkspaceProfileDocument is null
            || string.IsNullOrWhiteSpace(_activeWorkspaceProfileId))
        {
            return false;
        }

        var normalizedBindings = NormalizeWindowBindings(new[] { binding });
        if (normalizedBindings.Count == 0)
        {
            return false;
        }

        _activeWorkspaceProfileDocument = NormalizeWorkspaceProfile(
            UpsertWindowBindingPatch(_activeWorkspaceProfileDocument, normalizedBindings[0]));
        RaiseWindowBindingStatusChanged();
        SetStatusMessage(statusMessage);
        SaveWorkspaceProfileDocumentInBackground(
            _activeWorkspaceProfileId,
            _activeWorkspaceProfileDocument,
            statusMessage);
        return true;
    }

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
            WorkspaceProfiles.Add(new LayoutListItemViewModel
            {
                Id = item.Id,
                Name = item.Name,
                Description = item.BindingCount <= 0
                    ? $"空绑定  |  关联分区：{ResolveLayoutDisplayName(item.LayoutId)}"
                    : $"已绑定 {item.BindingCount} 个窗口  |  关联分区：{ResolveLayoutDisplayName(item.LayoutId)}",
                IsEmptyWorkspaceBinding = item.BindingCount <= 0,
                HasWorkspaceBinding = item.BindingCount > 0
            });
        }

        SelectedWorkspaceProfileItem = WorkspaceProfiles.FirstOrDefault(item =>
            string.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase));

        RaisePropertyChanged(nameof(WorkspaceProfileLabel));
    }

    private async void CreateWorkspaceProfileFromSelectedLayout()
    {
        if (_isSavingWorkspaceProfile)
        {
            return;
        }

        if (SelectedLayoutItem is null || string.IsNullOrWhiteSpace(SelectedLayoutItem.Id))
        {
            ShowErrorMessage("请先在已保存分区列表中选中一个分区，再创建工作区方案。");
            return;
        }

        var layoutId = SelectedLayoutItem.Id;
        var layoutName = SelectedLayoutItem.Name;
        var defaultName = $"{layoutName} 工作区";
        var enteredName = PromptForLayoutName("从选中的保存分区创建工作区", "请输入工作区名称。新工作区会关联当前选中的分区布局。", defaultName);
        if (enteredName is null)
        {
            return;
        }

        var targetName = NormalizeLayoutName(enteredName);
        var targetId = Slugify(targetName);
        var profile = new WorkspaceProfileDocument(
            2,
            targetName,
            layoutId,
            new List<WorkspaceWindowBinding>());

        _isSavingWorkspaceProfile = true;
        SetStatusMessage("正在从选中的保存分区创建工作区...");

        try
        {
            await SaveWorkspaceProfileToTargetAsync(
                targetId,
                profile,
                previousId: null,
                notifyOnSuccess: true);
        }
        finally
        {
            _isSavingWorkspaceProfile = false;
        }
    }

    private async void CreateWorkspaceProfileFromActiveSnapLayout()
    {
        if (_isSavingWorkspaceProfile)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_activeSnapLayoutId))
        {
            ShowErrorMessage("当前吸附选择还不是已保存分区，请先把分区保存后再创建工作区方案。");
            return;
        }

        var layoutId = _activeSnapLayoutId;
        var layoutName = _activeSnapLayoutName;
        var defaultName = $"{layoutName} 工作区";
        var enteredName = PromptForLayoutName("从当前吸附创建工作区", "请输入工作区名称。当前吸附选择会作为这套工作区的分区基础。", defaultName);
        if (enteredName is null)
        {
            return;
        }

        var targetName = NormalizeLayoutName(enteredName);
        var targetId = Slugify(targetName);
        var profile = new WorkspaceProfileDocument(
            2,
            targetName,
            layoutId,
            new List<WorkspaceWindowBinding>());

        _isSavingWorkspaceProfile = true;
        SetStatusMessage("正在从当前吸附创建工作区...");

        try
        {
            await SaveWorkspaceProfileToTargetAsync(
                targetId,
                profile,
                previousId: null,
                notifyOnSuccess: true);
        }
        finally
        {
            _isSavingWorkspaceProfile = false;
        }
    }

    private async void SaveWorkspaceProfile()
    {
        if (_isSavingWorkspaceProfile)
        {
            return;
        }

        if (!IsWorkspaceProfileEnabled
            || _activeWorkspaceProfileDocument is null
            || string.IsNullOrWhiteSpace(_activeWorkspaceProfileId))
        {
            ShowErrorMessage("请先从当前吸附或选中的保存分区创建工作区方案。");
            return;
        }

        _isSavingWorkspaceProfile = true;
        SetStatusMessage("正在保存工作区...");

        try
        {
            await SaveWorkspaceProfileToTargetAsync(
                _activeWorkspaceProfileId,
                _activeWorkspaceProfileDocument,
                previousId: _activeWorkspaceProfileId,
                notifyOnSuccess: false);
        }
        finally
        {
            _isSavingWorkspaceProfile = false;
        }
    }

    private async void SaveWorkspaceProfileAs()
    {
        if (_isSavingWorkspaceProfile)
        {
            return;
        }

        if (!TryBuildWorkspaceProfileDraft(out var profileDraft, out var message))
        {
            ShowErrorMessage(message);
            return;
        }

        var enteredName = PromptForLayoutName("工作区另存为", "请输入新的工作区名称。", profileDraft.Name);
        if (enteredName is null)
        {
            return;
        }

        var targetName = NormalizeLayoutName(enteredName);
        var targetId = Slugify(targetName);
        _isSavingWorkspaceProfile = true;
        SetStatusMessage("正在另存工作区...");

        try
        {
            await SaveWorkspaceProfileToTargetAsync(
                targetId,
                profileDraft with { Name = targetName },
                previousId: null,
                notifyOnSuccess: false);
        }
        finally
        {
            _isSavingWorkspaceProfile = false;
        }
    }

    private async Task SaveWorkspaceProfileToTargetAsync(
        string targetId,
        WorkspaceProfileDocument profile,
        string? previousId,
        bool notifyOnSuccess)
    {
        var shouldOverwrite = WorkspaceProfiles.Any(item =>
            string.Equals(item.Id, targetId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Id, previousId, StringComparison.OrdinalIgnoreCase));

        if (shouldOverwrite)
        {
            var overwrite = ShowMessage($"已存在名为“{profile.Name}”的工作区，是否覆盖？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (overwrite != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var normalizedProfile = NormalizeWorkspaceProfile(profile);
        if (string.IsNullOrWhiteSpace(normalizedProfile.LayoutId))
        {
            ShowErrorMessage("工作区还没有绑定分区布局，请先从分区布局创建工作区。");
            return;
        }

        try
        {
            await _workspaceProfileRepository.SaveAsync(targetId, normalizedProfile, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"保存工作区失败：{ex.Message}");
            return;
        }

        SetActiveWorkspaceProfile(targetId, normalizedProfile.Name, normalizedProfile);
        SaveSessionState();

        await RefreshWorkspaceProfilesAsync();
        SelectedWorkspaceProfileItem = WorkspaceProfiles.FirstOrDefault(item =>
            string.Equals(item.Id, targetId, StringComparison.OrdinalIgnoreCase));

        if (notifyOnSuccess)
        {
            ShowInfoMessage($"工作区“{normalizedProfile.Name}”已保存。");
        }
        else
        {
            SetStatusMessage($"工作区“{normalizedProfile.Name}”已保存");
        }
    }

    private void DeleteSelectedWorkspaceProfile()
    {
        if (SelectedWorkspaceProfileItem is null)
        {
            return;
        }

        var deletedProfileId = SelectedWorkspaceProfileItem.Id;
        var confirmed = ShowMessage($"确定删除工作区“{SelectedWorkspaceProfileItem.Name}”吗？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _workspaceProfileRepository
                .DeleteAsync(deletedProfileId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (string.Equals(_activeWorkspaceProfileId, deletedProfileId, StringComparison.OrdinalIgnoreCase))
            {
                SetActiveWorkspaceProfile(null, "未启用工作区方案", null);
            }

            RefreshWorkspaceProfiles();
            SaveSessionState();
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"删除工作区失败：{ex.Message}");
        }
    }

    private bool TryBuildWorkspaceProfileDraft(out WorkspaceProfileDocument profileDraft, out string message)
    {
        message = string.Empty;

        if (IsWorkspaceProfileEnabled && _activeWorkspaceProfileDocument is not null)
        {
            profileDraft = NormalizeWorkspaceProfile(_activeWorkspaceProfileDocument);
            return true;
        }

        if (!TryResolveWorkspaceProfileLayoutId(out var layoutId, out var layoutName, out message))
        {
            profileDraft = default!;
            return false;
        }

        profileDraft = new WorkspaceProfileDocument(
            2,
            $"{layoutName} 工作区方案",
            layoutId,
            new List<WorkspaceWindowBinding>());

        return true;
    }

    private bool TryResolveWorkspaceProfileLayoutIdPreferSelected(
        out string layoutId,
        out string layoutName,
        out string message)
    {
        layoutId = string.Empty;
        layoutName = string.Empty;
        message = string.Empty;

        if (SelectedLayoutItem is not null && !string.IsNullOrWhiteSpace(SelectedLayoutItem.Id))
        {
            layoutId = SelectedLayoutItem.Id;
            layoutName = SelectedLayoutItem.Name;
            return true;
        }

        return TryResolveWorkspaceProfileLayoutId(out layoutId, out layoutName, out message);
    }

    private bool TryResolveWorkspaceProfileLayoutId(out string layoutId, out string layoutName, out string message)
    {
        layoutId = string.Empty;
        layoutName = string.Empty;
        message = string.Empty;

        if (!string.IsNullOrWhiteSpace(_currentLayoutId))
        {
            layoutId = _currentLayoutId;
            layoutName = _currentWorkspaceDocument.Name;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_activeSnapLayoutId))
        {
            layoutId = _activeSnapLayoutId;
            layoutName = _activeSnapLayoutName;
            return true;
        }

        message = "请先把分区布局保存成文件，再创建工作区方案。";
        return false;
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

    private string ResolveLayoutDisplayName(string? layoutId)
    {
        if (string.IsNullOrWhiteSpace(layoutId))
        {
            return "未绑定分区布局";
        }

        var item = Layouts.FirstOrDefault(existing =>
            string.Equals(existing.Id, layoutId, StringComparison.OrdinalIgnoreCase));

        return item is null
            ? $"{layoutId}.json（未找到）"
            : item.Name;
    }

    private static WorkspaceProfileDocument NormalizeWorkspaceProfile(WorkspaceProfileDocument profile)
    {
        return profile with
        {
            Version = Math.Max(2, profile.Version),
            Name = NormalizeLayoutName(profile.Name),
            LayoutId = profile.LayoutId?.Trim() ?? string.Empty,
            WindowBindings = NormalizeWindowBindings(profile.WindowBindings)
        };
    }

    private static WorkspaceWindowBinding? GetWindowBinding(WorkspaceProfileDocument profile, string displayId, string nodeId)
    {
        return GetWindowBindings(profile, displayId, nodeId)
            .OrderBy(item => item.StackOrder)
            .LastOrDefault();
    }

    private static List<WorkspaceWindowBinding> GetWindowBindings(WorkspaceProfileDocument profile, string displayId, string nodeId)
    {
        return NormalizeWindowBindings(profile.WindowBindings)
            .Where(item =>
                string.Equals(item.DisplayId, displayId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.StackOrder)
            .ToList();
    }

    private static WorkspaceProfileDocument ReplaceWindowBindingsForRegion(
        WorkspaceProfileDocument profile,
        WorkspaceWindowBinding binding)
    {
        var bindings = RemoveWindowBinding(profile, binding.DisplayId, binding.NodeId).WindowBindings
            ?? new List<WorkspaceWindowBinding>();
        bindings.Add(binding with { StackOrder = 0 });
        return profile with { WindowBindings = bindings };
    }

    private static WorkspaceProfileDocument ReplaceWindowBindingGroups(
        WorkspaceProfileDocument profile,
        IReadOnlyList<WorkspaceWindowBinding> incomingBindings)
    {
        var bindings = NormalizeWindowBindings(profile.WindowBindings);
        var affectedRegionKeys = incomingBindings
            .Select(item => $"{item.DisplayId}::{item.NodeId}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bindings = bindings
            .Where(item => !affectedRegionKeys.Contains($"{item.DisplayId}::{item.NodeId}"))
            .ToList();

        foreach (var group in incomingBindings.GroupBy(item => $"{item.DisplayId}::{item.NodeId}", StringComparer.OrdinalIgnoreCase))
        {
            var stackOrder = 0;
            foreach (var binding in group.OrderBy(item => item.StackOrder))
            {
                bindings.Add(binding with { StackOrder = stackOrder });
                stackOrder++;
            }
        }

        return profile with { WindowBindings = bindings };
    }

    private static WorkspaceProfileDocument UpsertWindowBindingPatch(WorkspaceProfileDocument profile, WorkspaceWindowBinding binding)
    {
        var bindings = NormalizeWindowBindings(profile.WindowBindings);
        var index = bindings.FindIndex(item => IsSameWindowBindingSlot(item, binding));

        if (index >= 0)
        {
            bindings[index] = binding;
        }
        else
        {
            bindings.Add(binding);
        }

        return profile with { WindowBindings = bindings };
    }

    private static bool IsSameWindowBindingSlot(WorkspaceWindowBinding left, WorkspaceWindowBinding right)
    {
        return string.Equals(left.DisplayId, right.DisplayId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.NodeId, right.NodeId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.WindowTitleSnapshot, right.WindowTitleSnapshot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            && left.StackOrder == right.StackOrder;
    }

    private static bool IsExplorerFolderBinding(WorkspaceWindowBinding binding)
    {
        return string.Equals(binding.MatchKind, "ExplorerFolder", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(binding.LaunchTarget);
    }

    private static bool IsExplorerProcess(string processName)
    {
        return string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, "explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFolderPath(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch
        {
        }

        return TrimTrailingDirectorySeparators(normalized);
    }

    private static string TrimTrailingDirectorySeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(root)
            && string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrWhiteSpace(trimmed) ? path : trimmed;
    }

    private bool TrySaveActiveWorkspaceProfileDocument(out string message)
    {
        message = string.Empty;
        if (!IsWorkspaceProfileEnabled
            || _activeWorkspaceProfileDocument is null
            || string.IsNullOrWhiteSpace(_activeWorkspaceProfileId))
        {
            message = "当前没有可保存的工作区。";
            return false;
        }

        try
        {
            var normalizedProfile = NormalizeWorkspaceProfile(_activeWorkspaceProfileDocument);
            _workspaceProfileRepository
                .SaveAsync(_activeWorkspaceProfileId, normalizedProfile, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            SetActiveWorkspaceProfile(_activeWorkspaceProfileId, normalizedProfile.Name, normalizedProfile);
            RefreshWorkspaceProfiles();
            SelectedWorkspaceProfileItem = WorkspaceProfiles.FirstOrDefault(item =>
                string.Equals(item.Id, _activeWorkspaceProfileId, StringComparison.OrdinalIgnoreCase));
            SaveSessionState();
            SetStatusMessage($"工作区“{normalizedProfile.Name}”已保存");
            return true;
        }
        catch (Exception ex)
        {
            message = $"保存工作区失败：{ex.Message}";
            return false;
        }
    }

    private void SaveWorkspaceProfileDocumentInBackground(
        string profileId,
        WorkspaceProfileDocument profile,
        string successMessage)
    {
        var normalizedProfile = NormalizeWorkspaceProfile(profile);
        _ = Task.Run(() =>
        {
            try
            {
                _workspaceProfileRepository
                    .SaveAsync(profileId, normalizedProfile, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                return (Exception?)null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }).ContinueWith(task =>
        {
            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }

            dispatcher.BeginInvoke(() =>
            {
                if (task.Result is { } exception)
                {
                    SetStatusMessage($"后台保存工作区失败：{exception.Message}");
                    return;
                }

                RefreshWorkspaceProfiles();
                SelectedWorkspaceProfileItem = WorkspaceProfiles.FirstOrDefault(item =>
                    string.Equals(item.Id, profileId, StringComparison.OrdinalIgnoreCase));
                SaveSessionState();
                SetStatusMessage(successMessage);
            });
        }, TaskScheduler.Default);
    }

    private static WorkspaceProfileDocument RemoveWindowBinding(WorkspaceProfileDocument profile, string displayId, string nodeId)
    {
        var bindings = NormalizeWindowBindings(profile.WindowBindings)
            .Where(item =>
                !string.Equals(item.DisplayId, displayId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(item.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return profile with { WindowBindings = bindings };
    }
}
