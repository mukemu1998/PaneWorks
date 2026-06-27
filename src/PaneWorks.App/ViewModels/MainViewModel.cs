using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using PaneWorks.App.Controls;
using PaneWorks.App.Diagnostics;
using PaneWorks.App.Views;
using PaneWorks.Core.Abstractions;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;
using PaneWorks.Infrastructure.Persistence;
using PaneWorks.Infrastructure.Windows;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace PaneWorks.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string DefaultBlankWorkspaceName = "起始空白布局";
    private readonly LayoutEditorService _editorService = new();
    private readonly LayoutTreeQueryService _queryService = new();
    private readonly DisplayDiscoveryService _displayDiscoveryService = new();
    private readonly ILayoutRepository _layoutRepository;
    private readonly JsonAppSettingsRepository _appSettingsRepository;
    private readonly JsonSessionStateRepository _sessionStateRepository;
    private readonly StartupRegistrationService _startupRegistrationService = new();
    private readonly WorkspaceApplyService _workspaceApplyService = new();
    private readonly RelayCommand _undoCommand;
    private readonly RelayCommand _redoCommand;
    private readonly Stack<EditorHistoryState> _undoStack = new();
    private readonly Stack<EditorHistoryState> _redoStack = new();
    private readonly Dictionary<string, DisplayInfo> _displaysById = new(StringComparer.OrdinalIgnoreCase);

    private AppSettings _appSettings;
    private WorkspaceLayoutDocument _currentWorkspaceDocument;
    private WorkspaceLayoutDocument _activeSnapWorkspaceDocument;
    private LayoutDocument _currentDocument;
    private PersistedWorkspaceState _savedState;
    private string? _currentLayoutId;
    private string? _activeSnapLayoutId;
    private string _activeSnapLayoutName;
    private string? _selectedNodeId;
    private LayoutListItemViewModel? _selectedLayoutItem;
    private DisplayItemViewModel? _selectedDisplayItem;
    private bool _isDirty;
    private bool _suppressDisplaySelectionChange;
    private bool _isSavingLayout;
    private string _lastStatusMessage = string.Empty;

    public MainViewModel()
    {
        _layoutRepository = new JsonLayoutRepository(LayoutStoragePaths.GetDefaultLayoutsDirectory());
        _appSettingsRepository = new JsonAppSettingsRepository(LayoutStoragePaths.GetDefaultAppSettingsFilePath());
        _sessionStateRepository = new JsonSessionStateRepository(LayoutStoragePaths.GetDefaultSessionStateFilePath());
        _appSettings = LoadAppSettings();

        Layouts = new ObservableCollection<LayoutListItemViewModel>();
        Displays = new ObservableCollection<DisplayItemViewModel>();

        RefreshDisplays();

        _currentWorkspaceDocument = CreateWorkspaceDocument(DefaultBlankWorkspaceName);
        _activeSnapWorkspaceDocument = _currentWorkspaceDocument;
        _currentDocument = GetDisplayLayout(_currentWorkspaceDocument, GetPrimaryDisplayId());
        _activeSnapLayoutName = "当前编辑布局";
        _selectedNodeId = _currentDocument.Root.Id;
        _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);

        _undoCommand = new RelayCommand(Undo, () => _undoStack.Count > 0);
        _redoCommand = new RelayCommand(Redo, () => _redoStack.Count > 0);

        NewLayoutCommand = new RelayCommand(CreateNewLayout);
        UndoCommand = _undoCommand;
        RedoCommand = _redoCommand;
        ApplyLayoutCommand = new RelayCommand(ApplyCurrentLayoutToWindows);
        SaveLayoutCommand = new RelayCommand(SaveCurrentLayout);
        SaveAsLayoutCommand = new RelayCommand(SaveCurrentLayoutAs);
        SetSelectedLayoutAsSnapLayoutCommand = new RelayCommand(SetSelectedLayoutAsSnapLayout, () => SelectedLayoutItem is not null);
        LoadSelectedLayoutCommand = new RelayCommand(LoadSelectedLayout);
        DeleteSelectedLayoutCommand = new RelayCommand(DeleteSelectedLayout);
        SplitHorizontalCommand = new RelayCommand(() => SplitLeafById(SelectedNodeId, SplitDirection.Horizontal));
        SplitVerticalCommand = new RelayCommand(() => SplitLeafById(SelectedNodeId, SplitDirection.Vertical));
        DeleteSelectedSplitCommand = new RelayCommand(() => DeleteContainingSplit(SelectedNodeId));

        RefreshDisplays();
        RefreshLayouts();
        TryRestoreLastLayoutOnStartup();
    }

    public ObservableCollection<LayoutListItemViewModel> Layouts { get; }

    public ObservableCollection<DisplayItemViewModel> Displays { get; }

    public ICommand NewLayoutCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

    public ICommand ApplyLayoutCommand { get; }

    public ICommand SaveLayoutCommand { get; }

    public ICommand SaveAsLayoutCommand { get; }

    public ICommand SetSelectedLayoutAsSnapLayoutCommand { get; }

    public ICommand LoadSelectedLayoutCommand { get; }

    public ICommand DeleteSelectedLayoutCommand { get; }

    public ICommand SplitHorizontalCommand { get; }

    public ICommand SplitVerticalCommand { get; }

    public ICommand DeleteSelectedSplitCommand { get; }

    public LayoutDocument CurrentDocument
    {
        get => _currentDocument;
        private set
        {
            if (SetProperty(ref _currentDocument, value))
            {
                RaisePropertyChanged(nameof(CanvasSubtitle));
                RaisePropertyChanged(nameof(StatusLine));
                RaisePropertyChanged(nameof(SelectionLabel));
            }
        }
    }

    public string CurrentLayoutName
    {
        get => _currentWorkspaceDocument.Name;
        set
        {
            var normalized = NormalizeLayoutName(value);
            if (normalized == _currentWorkspaceDocument.Name)
            {
                return;
            }

            PushUndoState();
            _redoStack.Clear();
            _currentWorkspaceDocument = RenameWorkspace(_currentWorkspaceDocument, normalized);
            SyncActiveSnapWithCurrentWorkspaceIfNeeded();
            CurrentDocument = GetDisplayLayout(_currentWorkspaceDocument, SelectedDisplayItem?.Id);
            UpdateDirtyState();
            UpdateHistoryCommandStates();
        }
    }

    public string? SelectedNodeId
    {
        get => _selectedNodeId;
        private set
        {
            if (SetProperty(ref _selectedNodeId, value))
            {
                RaisePropertyChanged(nameof(SelectionLabel));
            }
        }
    }

    public LayoutListItemViewModel? SelectedLayoutItem
    {
        get => _selectedLayoutItem;
        set
        {
            if (SetProperty(ref _selectedLayoutItem, value) && SetSelectedLayoutAsSnapLayoutCommand is RelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public DisplayItemViewModel? SelectedDisplayItem
    {
        get => _selectedDisplayItem;
        set
        {
            if (ReferenceEquals(_selectedDisplayItem, value))
            {
                return;
            }

            _selectedDisplayItem = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CurrentDisplayName));
            RaisePropertyChanged(nameof(CurrentDisplaySummary));
            RaisePropertyChanged(nameof(CanvasSubtitle));
            RaisePropertyChanged(nameof(StatusLine));

            if (_suppressDisplaySelectionChange || value is null)
            {
                return;
            }

            ActivateDisplay(value.Id, resetHistory: true);
            SaveSessionState();
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                RaisePropertyChanged(nameof(DirtyLabel));
            }
        }
    }

    public string DirtyLabel => IsDirty ? "未保存修改" : "已保存";

    public string ActiveSnapLayoutName => _activeSnapLayoutName;

    public string ActiveSnapLayoutId => _activeSnapLayoutId ?? string.Empty;

    public string SnapModifierKey => _appSettings.SnapModifierKey;

    public string MinimizeShortcut => _appSettings.MinimizeShortcut;

    public string CurrentDisplayName => SelectedDisplayItem?.Name ?? "主屏幕";

    public string CurrentDisplaySummary => SelectedDisplayItem?.Description ?? string.Empty;

    public string CanvasSubtitle
    {
        get
        {
            var fileLabel = _currentLayoutId is null ? "当前是临时多屏草稿" : $"当前文件：{_currentLayoutId}.json";
            return $"编辑屏幕：{CurrentDisplayName}  |  {fileLabel}  |  同一个布局文件会同时保存所有屏幕的分割结果。";
        }
    }

    public string StatusLine => string.IsNullOrWhiteSpace(_lastStatusMessage)
        ? CanvasSubtitle
        : $"{CanvasSubtitle}  |  最近操作：{_lastStatusMessage}";

    public string SnapLayoutLabel
    {
        get
        {
            var idLabel = string.IsNullOrWhiteSpace(_activeSnapLayoutId) ? "未绑定到已保存布局" : $"{_activeSnapLayoutId}.json";
            return $"当前吸附布局：{_activeSnapLayoutName}  |  {idLabel}  |  会在所有屏幕统一生效";
        }
    }

    public string SelectionLabel
    {
        get
        {
            if (SelectedNodeId is null)
            {
                return "当前没有选中区域或分割线。";
            }

            if (_queryService.IsLeaf(CurrentDocument, SelectedNodeId))
            {
                return $"当前选中区域：{SelectedNodeId}";
            }

            if (_queryService.IsSplit(CurrentDocument, SelectedNodeId))
            {
                return $"当前选中分割线：{SelectedNodeId}";
            }

            return $"当前选中节点：{SelectedNodeId}";
        }
    }

    public string ShortcutSummary
    {
        get
        {
            var startupLabel = _appSettings.LaunchAtStartup ? "已开启" : "未开启";
            var snapLabel = ShortcutGestureHelper.ToDisplayString(_appSettings.SnapModifierKey, "Shift");
            var minimizeLabel = ShortcutGestureHelper.ToDisplayString(_appSettings.MinimizeShortcut, "Esc");
            return $"吸附触发键：{snapLabel}  |  最小化快捷键：{minimizeLabel}  |  开机自启：{startupLabel}";
        }
    }

    public void SelectNode(string nodeId)
    {
        SelectedNodeId = nodeId;
    }

    public DisplayInfo GetSelectedDisplay()
    {
        return GetDisplayOrPrimary(SelectedDisplayItem?.Id);
    }

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        return _displaysById.Values
            .OrderBy(display => display.Bounds.X)
            .ThenBy(display => display.Bounds.Y)
            .ToList();
    }

    public bool TryGetDisplayById(string? displayId, out DisplayInfo display)
    {
        if (!string.IsNullOrWhiteSpace(displayId) && _displaysById.TryGetValue(displayId, out display!))
        {
            return true;
        }

        display = GetSelectedDisplay();
        return false;
    }

    public LayoutDocument GetSnapLayoutDocumentForDisplay(string displayId)
    {
        return GetDisplayLayout(_activeSnapWorkspaceDocument, displayId);
    }

    public LayoutDocument GetCurrentLayoutDocumentForDisplay(string displayId)
    {
        return GetDisplayLayout(_currentWorkspaceDocument, displayId);
    }

    public void UpdateSplitRatio(string splitNodeId, double ratio)
    {
        var result = _editorService.UpdateSplitRatio(CurrentDocument, splitNodeId, ratio);
        if (!result.Changed)
        {
            return;
        }

        ApplyEditorMutation(result.Document, result.SelectedNodeId);
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

    public void ResetSnapLayoutPreview()
    {
    }

    public void OpenSettings()
    {
        var dialog = new SettingsDialog(_appSettings);
        var owner = GetOwnerWindow();
        if (owner is not null)
        {
            PrepareOwnerWindow(owner);
            dialog.Owner = owner;
            dialog.Topmost = owner.Topmost;
        }

        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            return;
        }

        var updatedSettings = NormalizeAppSettings(new AppSettings(
            dialog.Result.SnapModifierKey,
            dialog.Result.MinimizeShortcut,
            dialog.Result.LaunchAtStartup));

        try
        {
            _startupRegistrationService.SetEnabled(updatedSettings.LaunchAtStartup);
            _appSettings = updatedSettings with
            {
                LaunchAtStartup = _startupRegistrationService.IsEnabled()
            };

            _appSettingsRepository.Save(_appSettings);
            RaisePropertyChanged(nameof(SnapModifierKey));
            RaisePropertyChanged(nameof(MinimizeShortcut));
            RaisePropertyChanged(nameof(ShortcutSummary));
            ShowInfoMessage("设置已保存，新的快捷键和开机自启状态已经生效。");
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"保存设置失败：{ex.Message}");
        }
    }

    public void SwitchSnapLayout(string layoutId, bool notifyOnSuccess)
    {
        if (string.IsNullOrWhiteSpace(layoutId))
        {
            return;
        }

        TrySetSnapLayout(layoutId, notifyOnSuccess);
    }

    public void HandleCanvasContextAction(CanvasContextAction action, string targetNodeId)
    {
        SelectNode(targetNodeId);

        switch (action)
        {
            case CanvasContextAction.SplitHorizontalHalf:
                SplitLeafById(targetNodeId, SplitDirection.Horizontal);
                break;
            case CanvasContextAction.SplitVerticalHalf:
                SplitLeafById(targetNodeId, SplitDirection.Vertical);
                break;
            case CanvasContextAction.SplitHorizontalThirds:
                SplitLeafIntoThirds(targetNodeId, SplitDirection.Horizontal);
                break;
            case CanvasContextAction.SplitVerticalThirds:
                SplitLeafIntoThirds(targetNodeId, SplitDirection.Vertical);
                break;
            case CanvasContextAction.Delete:
                DeleteContainingSplit(targetNodeId);
                break;
        }
    }

    public bool TryClose()
    {
        return ResolvePendingChanges("关闭 PaneWorks");
    }

    private void CreateNewLayout()
    {
        if (!ResolvePendingChanges("新建布局"))
        {
            return;
        }

        _currentWorkspaceDocument = CreateWorkspaceDocument(DefaultBlankWorkspaceName);
        _currentLayoutId = null;
        _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);
        IsDirty = false;
        ResetHistory();
        SyncActiveSnapWithCurrentWorkspaceIfNeeded();
        ActivateDisplay(SelectedDisplayItem?.Id ?? GetPrimaryDisplayId(), resetHistory: false);
        SaveSessionState();
    }

    private async void SaveCurrentLayout()
    {
        if (_isSavingLayout)
        {
            return;
        }

        var targetId = Slugify(CurrentLayoutName);
        _isSavingLayout = true;
        SetStatusMessage("正在保存布局...");

        try
        {
            await SaveToTargetAsync(targetId, CurrentLayoutName, treatAsSaveAs: false, notifyOnSuccess: false);
        }
        finally
        {
            _isSavingLayout = false;
        }
    }

    private void ApplyCurrentLayoutToWindows()
    {
        try
        {
            var owner = GetOwnerWindow();
            var excludedHandle = owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
            var targetDisplay = GetSelectedDisplay();
            var result = _workspaceApplyService.Apply(CurrentDocument, targetDisplay.WorkArea, excludedHandle);

            if (result.AppliedWindowCount == 0)
            {
                ShowInfoMessage("没有找到可排列的桌面窗口。请先打开几个普通应用窗口再试。");
                return;
            }

            ShowInfoMessage($"已按 {targetDisplay.Name} 当前布局排列 {result.AppliedWindowCount} 个窗口。多屏整包保存已经生效，应用按钮仍按当前屏幕单独测试。");
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"应用布局失败：{ex.Message}");
        }
    }

    private async void SaveCurrentLayoutAs()
    {
        if (_isSavingLayout)
        {
            return;
        }

        var enteredName = PromptForLayoutName("布局另存为", "请输入新布局名称。", CurrentLayoutName);
        if (enteredName is null)
        {
            return;
        }

        var targetName = NormalizeLayoutName(enteredName);
        var targetId = Slugify(targetName);
        _isSavingLayout = true;
        SetStatusMessage("正在另存布局...");

        try
        {
            await SaveToTargetAsync(targetId, targetName, treatAsSaveAs: true, notifyOnSuccess: false);
        }
        finally
        {
            _isSavingLayout = false;
        }
    }

    private void SetSelectedLayoutAsSnapLayout()
    {
        if (SelectedLayoutItem is null)
        {
            return;
        }

        TrySetSnapLayout(SelectedLayoutItem.Id, notifyOnSuccess: true);
    }

    private void LoadSelectedLayout()
    {
        if (SelectedLayoutItem is null)
        {
            return;
        }

        if (!ResolvePendingChanges("打开选中的布局"))
        {
            return;
        }

        try
        {
            var workspace = NormalizeWorkspaceForCurrentDisplays(
                _layoutRepository
                    .LoadAsync(SelectedLayoutItem.Id, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

            _currentWorkspaceDocument = workspace;
            _currentLayoutId = SelectedLayoutItem.Id;
            _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);
            IsDirty = false;
            ResetHistory();
            SyncActiveSnapWithCurrentWorkspaceIfNeeded();
            ActivateDisplay(SelectedDisplayItem?.Id ?? GetPrimaryDisplayId(), resetHistory: false);
            SaveSessionState();
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"打开布局失败：{ex.Message}");
        }
    }

    private void DeleteSelectedLayout()
    {
        if (SelectedLayoutItem is null)
        {
            return;
        }

        var deletedLayoutId = SelectedLayoutItem.Id;
        var confirmed = ShowMessage($"确定删除布局“{SelectedLayoutItem.Name}”吗？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
            SaveSessionState();
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"删除布局失败：{ex.Message}");
        }
    }

    private void SplitLeafById(string? nodeId, SplitDirection direction)
    {
        if (!_queryService.IsLeaf(CurrentDocument, nodeId))
        {
            return;
        }

        var result = _editorService.SplitLeaf(CurrentDocument, nodeId!, direction);
        if (!result.Changed)
        {
            return;
        }

        ApplyEditorMutation(result.Document, result.SelectedNodeId);
    }

    private void SplitLeafIntoThirds(string? nodeId, SplitDirection direction)
    {
        if (!_queryService.IsLeaf(CurrentDocument, nodeId))
        {
            return;
        }

        var result = _editorService.SplitLeafIntoThree(CurrentDocument, nodeId!, direction);
        if (!result.Changed)
        {
            return;
        }

        ApplyEditorMutation(result.Document, result.SelectedNodeId);
    }

    private void DeleteContainingSplit(string? nodeId)
    {
        if (nodeId is null)
        {
            return;
        }

        var splitId = _queryService.IsSplit(CurrentDocument, nodeId)
            ? nodeId
            : _queryService.FindParentSplitId(CurrentDocument, nodeId);

        if (splitId is null)
        {
            return;
        }

        var result = _editorService.DeleteSplit(CurrentDocument, splitId);
        if (!result.Changed)
        {
            return;
        }

        ApplyEditorMutation(result.Document, result.SelectedNodeId);
    }

    private bool SaveToTarget(string targetId, string targetName, bool treatAsSaveAs, bool notifyOnSuccess)
    {
        var previousId = _currentLayoutId;
        var tracksCurrentAsSnap = ShouldSyncActiveSnapWithCurrentWorkspace();
        var willRenameCurrentFile = !treatAsSaveAs
            && !string.IsNullOrWhiteSpace(previousId)
            && !string.Equals(previousId, targetId, StringComparison.OrdinalIgnoreCase);

        var shouldOverwrite = Layouts.Any(item =>
            string.Equals(item.Id, targetId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Id, previousId, StringComparison.OrdinalIgnoreCase));

        if (shouldOverwrite)
        {
            var overwrite = ShowMessage($"已存在名为“{targetName}”的布局，是否覆盖？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (overwrite != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        var workspaceToSave = RenameWorkspace(_currentWorkspaceDocument, targetName);

        try
        {
            PaneWorksLog.Info($"Save layout sync start: {targetId}");
            _layoutRepository
                .SaveAsync(targetId, workspaceToSave, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (willRenameCurrentFile)
            {
                _layoutRepository
                    .DeleteAsync(previousId!, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            PaneWorksLog.Info($"Save layout sync done: {targetId}");
        }
        catch (Exception ex)
        {
            PaneWorksLog.Error($"Save layout sync failed: {targetId}", ex);
            ShowErrorMessage($"保存布局失败：{ex.Message}");
            return false;
        }

        _currentWorkspaceDocument = workspaceToSave;
        _currentLayoutId = targetId;
        _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);
        IsDirty = false;

        if (tracksCurrentAsSnap)
        {
            SetActiveSnapWorkspace(targetId, targetName, _currentWorkspaceDocument);
        }

        ActivateDisplay(SelectedDisplayItem?.Id ?? GetPrimaryDisplayId(), resetHistory: false);
        SaveSessionState();

        RefreshLayouts();
        SelectedLayoutItem = Layouts.FirstOrDefault(item => item.Id == _currentLayoutId);

        if (notifyOnSuccess)
        {
            ShowInfoMessage($"多屏布局“{targetName}”已保存。当前所有屏幕的编辑结果都会写进同一个文件。");
        }

        if (!notifyOnSuccess)
        {
            SetStatusMessage($"多屏布局“{targetName}”已保存");
        }

        return true;
    }

    private async Task<bool> SaveToTargetAsync(string targetId, string targetName, bool treatAsSaveAs, bool notifyOnSuccess)
    {
        var previousId = _currentLayoutId;
        var tracksCurrentAsSnap = ShouldSyncActiveSnapWithCurrentWorkspace();
        var willRenameCurrentFile = !treatAsSaveAs
            && !string.IsNullOrWhiteSpace(previousId)
            && !string.Equals(previousId, targetId, StringComparison.OrdinalIgnoreCase);

        var shouldOverwrite = Layouts.Any(item =>
            string.Equals(item.Id, targetId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Id, previousId, StringComparison.OrdinalIgnoreCase));

        if (shouldOverwrite)
        {
            var overwrite = ShowMessage($"已存在名为“{targetName}”的布局，是否覆盖？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (overwrite != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        var workspaceToSave = RenameWorkspace(_currentWorkspaceDocument, targetName);

        try
        {
            PaneWorksLog.Info($"Save layout async start: {targetId}");
            await _layoutRepository
                .SaveAsync(targetId, workspaceToSave, CancellationToken.None);

            if (willRenameCurrentFile)
            {
                await _layoutRepository
                    .DeleteAsync(previousId!, CancellationToken.None);
            }
            PaneWorksLog.Info($"Save layout async done: {targetId}");
        }
        catch (Exception ex)
        {
            PaneWorksLog.Error($"Save layout async failed: {targetId}", ex);
            ShowErrorMessage($"保存布局失败：{ex.Message}");
            return false;
        }

        _currentWorkspaceDocument = workspaceToSave;
        _currentLayoutId = targetId;
        _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);
        IsDirty = false;

        if (tracksCurrentAsSnap)
        {
            SetActiveSnapWorkspace(targetId, targetName, _currentWorkspaceDocument);
        }

        ActivateDisplay(SelectedDisplayItem?.Id ?? GetPrimaryDisplayId(), resetHistory: false);
        SaveSessionState();

        await RefreshLayoutsAsync();
        SelectedLayoutItem = Layouts.FirstOrDefault(item => item.Id == _currentLayoutId);

        if (notifyOnSuccess)
        {
            ShowInfoMessage($"多屏布局“{targetName}”已保存。当前所有屏幕的编辑结果都会写进同一个文件。");
        }

        if (!notifyOnSuccess)
        {
            SetStatusMessage($"多屏布局“{targetName}”已保存");
        }

        return true;
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Push(new EditorHistoryState(_currentWorkspaceDocument, SelectedNodeId));
        RestoreHistoryState(_undoStack.Pop());
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(new EditorHistoryState(_currentWorkspaceDocument, SelectedNodeId));
        RestoreHistoryState(_redoStack.Pop());
    }

    private void RefreshDisplays()
    {
        var discoveredDisplays = _displayDiscoveryService.GetDisplays();
        var previousDisplayId = _selectedDisplayItem?.Id;

        _displaysById.Clear();
        Displays.Clear();

        foreach (var display in discoveredDisplays)
        {
            _displaysById[display.Id] = display;
            Displays.Add(new DisplayItemViewModel
            {
                Id = display.Id,
                Name = display.Name,
                Description = $"{(display.IsPrimary ? "主屏" : "扩展屏")}  |  {display.Bounds.Width:0} × {display.Bounds.Height:0}"
            });
        }

        SetSelectedDisplayItemSilently(previousDisplayId ?? GetPrimaryDisplayId());
    }

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
                Description = $"多屏布局文件：{item.Id}.json"
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
        SaveSessionState();
    }

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

    private void ApplyEditorMutation(LayoutDocument document, string? selectedNodeId)
    {
        PushUndoState();
        _redoStack.Clear();
        _currentWorkspaceDocument = ReplaceDisplayLayout(_currentWorkspaceDocument, SelectedDisplayItem?.Id, document);
        CurrentDocument = document;
        SelectedNodeId = selectedNodeId;
        SyncActiveSnapWithCurrentWorkspaceIfNeeded();
        UpdateDirtyState();
        UpdateHistoryCommandStates();
    }

    private void RestoreHistoryState(EditorHistoryState state)
    {
        _currentWorkspaceDocument = state.WorkspaceDocument;
        CurrentDocument = GetDisplayLayout(_currentWorkspaceDocument, SelectedDisplayItem?.Id);
        SelectedNodeId = state.SelectedNodeId;
        SyncActiveSnapWithCurrentWorkspaceIfNeeded();
        UpdateDirtyState();
        UpdateHistoryCommandStates();
    }

    private void PushUndoState()
    {
        _undoStack.Push(new EditorHistoryState(_currentWorkspaceDocument, SelectedNodeId));
    }

    private void ResetHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        UpdateHistoryCommandStates();
    }

    private void UpdateDirtyState()
    {
        IsDirty = !Equals(_savedState, new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId));
    }

    private void UpdateHistoryCommandStates()
    {
        _undoCommand.RaiseCanExecuteChanged();
        _redoCommand.RaiseCanExecuteChanged();
    }

    private void SaveSessionState()
    {
        _sessionStateRepository.Save(new SessionState
        {
            LastLayoutId = _currentLayoutId,
            LastSnapLayoutId = _activeSnapLayoutId,
            SelectedDisplayId = SelectedDisplayItem?.Id
        });
    }

    private void SetStatusMessage(string message)
    {
        _lastStatusMessage = message;
        RaisePropertyChanged(nameof(StatusLine));
    }

    private AppSettings LoadAppSettings()
    {
        var savedSettings = NormalizeAppSettings(_appSettingsRepository.Load());
        var actualStartupState = _startupRegistrationService.IsEnabled();
        var normalizedSettings = savedSettings with
        {
            LaunchAtStartup = actualStartupState
        };

        if (!Equals(savedSettings, normalizedSettings))
        {
            _appSettingsRepository.Save(normalizedSettings);
        }

        return normalizedSettings;
    }

    private bool TrySetSnapLayout(string layoutId, bool notifyOnSuccess)
    {
        var selectedItem = Layouts.FirstOrDefault(item =>
            string.Equals(item.Id, layoutId, StringComparison.OrdinalIgnoreCase));

        if (selectedItem is null)
        {
            ShowErrorMessage("未找到要切换的吸附布局。");
            return false;
        }

        try
        {
            var workspace = NormalizeWorkspaceForCurrentDisplays(
                _layoutRepository.LoadAsync(selectedItem.Id, CancellationToken.None).GetAwaiter().GetResult());

            SelectedLayoutItem = selectedItem;
            SetActiveSnapWorkspace(selectedItem.Id, workspace.Name, workspace);
            SaveSessionState();

            if (notifyOnSuccess)
            {
                ShowInfoMessage($"当前吸附布局已切换为“{workspace.Name}”。这套多屏布局会直接用于后续所有屏幕吸附。");
            }

            if (!notifyOnSuccess)
            {
                SetStatusMessage($"当前吸附布局已切换为“{workspace.Name}”");
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"设置吸附布局失败：{ex.Message}");
            return false;
        }
    }

    private void ActivateDisplay(string? displayId, bool resetHistory)
    {
        if (!TryGetDisplayById(displayId, out var display))
        {
            return;
        }

        _currentWorkspaceDocument = NormalizeWorkspaceForCurrentDisplays(_currentWorkspaceDocument);
        CurrentDocument = GetDisplayLayout(_currentWorkspaceDocument, display.Id);
        SelectedNodeId = CurrentDocument.Root.Id;

        if (resetHistory)
        {
            ResetHistory();
        }

        SetSelectedDisplayItemSilently(display.Id);
        RaisePropertyChanged(nameof(CanvasSubtitle));
        RaisePropertyChanged(nameof(StatusLine));
    }

    private WorkspaceLayoutDocument CreateWorkspaceDocument(string name)
    {
        var normalizedName = NormalizeLayoutName(name);
        var layouts = new Dictionary<string, LayoutDocument>(StringComparer.OrdinalIgnoreCase);

        foreach (var display in _displaysById.Values)
        {
            layouts[display.Id] = _editorService.CreateBlank(normalizedName);
        }

        return new WorkspaceLayoutDocument(1, normalizedName, layouts);
    }

    private WorkspaceLayoutDocument NormalizeWorkspaceForCurrentDisplays(WorkspaceLayoutDocument workspace)
    {
        var normalizedName = NormalizeLayoutName(workspace.Name);
        var layouts = workspace.DisplayLayouts is null
            ? new Dictionary<string, LayoutDocument>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, LayoutDocument>(workspace.DisplayLayouts, StringComparer.OrdinalIgnoreCase);

        var exactMatches = layouts.Keys.Count(key => _displaysById.ContainsKey(key));
        var fallback = layouts.Values.FirstOrDefault();

        foreach (var display in _displaysById.Values)
        {
            if (layouts.ContainsKey(display.Id))
            {
                continue;
            }

            if (fallback is not null && exactMatches == 0 && display.IsPrimary)
            {
                layouts[display.Id] = fallback with { Name = normalizedName };
                continue;
            }

            layouts[display.Id] = _editorService.CreateBlank(normalizedName);
        }

        return new WorkspaceLayoutDocument(workspace.Version, normalizedName, layouts);
    }

    private LayoutDocument GetDisplayLayout(WorkspaceLayoutDocument workspace, string? displayId)
    {
        workspace = NormalizeWorkspaceForCurrentDisplays(workspace);
        var resolvedDisplayId = !string.IsNullOrWhiteSpace(displayId) && workspace.DisplayLayouts.ContainsKey(displayId)
            ? displayId
            : GetPrimaryDisplayId();

        if (workspace.DisplayLayouts.TryGetValue(resolvedDisplayId, out var layout))
        {
            return layout;
        }

        return workspace.DisplayLayouts.Values.FirstOrDefault() ?? _editorService.CreateBlank(workspace.Name);
    }

    private WorkspaceLayoutDocument ReplaceDisplayLayout(WorkspaceLayoutDocument workspace, string? displayId, LayoutDocument document)
    {
        workspace = NormalizeWorkspaceForCurrentDisplays(workspace);
        var resolvedDisplayId = !string.IsNullOrWhiteSpace(displayId) ? displayId : GetPrimaryDisplayId();
        var layouts = new Dictionary<string, LayoutDocument>(workspace.DisplayLayouts, StringComparer.OrdinalIgnoreCase)
        {
            [resolvedDisplayId] = document
        };

        return workspace with { DisplayLayouts = layouts };
    }

    private WorkspaceLayoutDocument RenameWorkspace(WorkspaceLayoutDocument workspace, string newName)
    {
        var normalizedName = NormalizeLayoutName(newName);
        var layouts = workspace.DisplayLayouts.ToDictionary(
            entry => entry.Key,
            entry => entry.Value with { Name = normalizedName },
            StringComparer.OrdinalIgnoreCase);

        return workspace with
        {
            Name = normalizedName,
            DisplayLayouts = layouts
        };
    }

    private void SetActiveSnapWorkspace(string? layoutId, string layoutName, WorkspaceLayoutDocument workspace)
    {
        _activeSnapLayoutId = layoutId;
        _activeSnapLayoutName = layoutName;
        _activeSnapWorkspaceDocument = NormalizeWorkspaceForCurrentDisplays(workspace);
        RaisePropertyChanged(nameof(ActiveSnapLayoutName));
        RaisePropertyChanged(nameof(ActiveSnapLayoutId));
        RaisePropertyChanged(nameof(SnapLayoutLabel));
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

    private void SetSelectedDisplayItemSilently(string displayId)
    {
        var selected = Displays.FirstOrDefault(item => string.Equals(item.Id, displayId, StringComparison.OrdinalIgnoreCase));
        _suppressDisplaySelectionChange = true;
        _selectedDisplayItem = selected;
        RaisePropertyChanged(nameof(SelectedDisplayItem));
        RaisePropertyChanged(nameof(CurrentDisplayName));
        RaisePropertyChanged(nameof(CurrentDisplaySummary));
        RaisePropertyChanged(nameof(CanvasSubtitle));
        RaisePropertyChanged(nameof(StatusLine));
        _suppressDisplaySelectionChange = false;
    }

    private string GetPrimaryDisplayId()
    {
        return _displaysById.Values.FirstOrDefault(display => display.IsPrimary)?.Id
            ?? _displaysById.Keys.First();
    }

    private DisplayInfo GetDisplayOrPrimary(string? displayId)
    {
        if (!string.IsNullOrWhiteSpace(displayId) && _displaysById.TryGetValue(displayId, out var display))
        {
            return display;
        }

        return _displayDiscoveryService.GetPrimaryDisplay();
    }

    private static AppSettings NormalizeAppSettings(AppSettings settings)
    {
        var snapModifier = ShortcutGestureHelper.NormalizeShortcut(
            settings.SnapModifierKey,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.SnapModifierKey, "Shift"));
        var minimizeShortcut = ShortcutGestureHelper.NormalizeShortcut(
            settings.MinimizeShortcut,
            ShortcutGestureHelper.NormalizeShortcut(AppSettings.Default.MinimizeShortcut, "Esc"));

        return settings with
        {
            SnapModifierKey = snapModifier,
            MinimizeShortcut = minimizeShortcut
        };
    }

    private static string NormalizeLayoutName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "未命名布局" : value.Trim();
    }

    private static string? PromptForLayoutName(string title, string message, string initialValue)
    {
        var dialog = new LayoutNameDialog(title, message, initialValue);
        var owner = GetOwnerWindow();
        if (owner is not null)
        {
            PrepareOwnerWindow(owner);
            dialog.Owner = owner;
            dialog.Topmost = owner.Topmost;
        }

        return dialog.ShowDialog() == true ? dialog.LayoutName : null;
    }

    private static MessageBoxResult ShowMessage(string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = GetOwnerWindow();
        PrepareOwnerWindow(owner);
        return owner is null
            ? WpfMessageBox.Show(message, "PaneWorks", buttons, image)
            : WpfMessageBox.Show(owner, message, "PaneWorks", buttons, image);
    }

    private static void ShowInfoMessage(string message)
    {
        ShowMessage(message, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void ShowErrorMessage(string message)
    {
        ShowMessage(message, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static Window? GetOwnerWindow()
    {
        return WpfApplication.Current?.Windows
            .OfType<Window>()
            .OrderByDescending(window => window.IsActive)
            .ThenByDescending(window => window.Topmost)
            .FirstOrDefault(window => window.IsVisible)
            ?? WpfApplication.Current?.MainWindow;
    }

    private static void PrepareOwnerWindow(Window? owner)
    {
        if (owner is null)
        {
            return;
        }

        if (owner.WindowState == WindowState.Minimized)
        {
            owner.WindowState = WindowState.Normal;
        }

        owner.Activate();
        owner.Focus();
    }

    private static string Slugify(string value)
    {
        var cleaned = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ch == ' ' || ch == '-' || ch == '_' ? '-' : '\0')
            .Where(ch => ch != '\0')
            .ToArray());

        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "layout" : cleaned;
    }

    private sealed record EditorHistoryState(WorkspaceLayoutDocument WorkspaceDocument, string? SelectedNodeId);

    private sealed record PersistedWorkspaceState(WorkspaceLayoutDocument WorkspaceDocument, string? LayoutId);
}
