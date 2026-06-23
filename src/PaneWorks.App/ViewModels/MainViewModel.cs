using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using PaneWorks.App.Controls;
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
    private readonly LayoutEditorService _editorService = new();
    private readonly LayoutTreeQueryService _queryService = new();
    private readonly ILayoutRepository _layoutRepository;
    private readonly JsonAppSettingsRepository _appSettingsRepository;
    private readonly JsonSessionStateRepository _sessionStateRepository;
    private readonly StartupRegistrationService _startupRegistrationService = new();
    private readonly WorkspaceApplyService _workspaceApplyService = new();
    private readonly RelayCommand _undoCommand;
    private readonly RelayCommand _redoCommand;
    private readonly Stack<EditorHistoryState> _undoStack = new();
    private readonly Stack<EditorHistoryState> _redoStack = new();

    private AppSettings _appSettings;
    private LayoutDocument _currentDocument;
    private LayoutDocument _activeSnapDocument;
    private LayoutDocument? _previewSnapDocument;
    private PersistedLayoutState _savedState;
    private string? _currentLayoutId;
    private string? _activeSnapLayoutId;
    private string _activeSnapLayoutName;
    private string? _selectedNodeId;
    private string? _previewNodeId;
    private bool _isDirty;
    private LayoutListItemViewModel? _selectedLayoutItem;

    public MainViewModel()
    {
        _layoutRepository = new JsonLayoutRepository(LayoutStoragePaths.GetDefaultLayoutsDirectory());
        _appSettingsRepository = new JsonAppSettingsRepository(LayoutStoragePaths.GetDefaultAppSettingsFilePath());
        _sessionStateRepository = new JsonSessionStateRepository(LayoutStoragePaths.GetDefaultSessionStateFilePath());
        _appSettings = LoadAppSettings();
        _currentDocument = _editorService.CreateBlank("起始布局");
        _activeSnapDocument = _currentDocument;
        _activeSnapLayoutName = "当前编辑布局";
        _selectedNodeId = _currentDocument.Root.Id;
        _savedState = new PersistedLayoutState(_currentDocument, _currentLayoutId);

        Layouts = new ObservableCollection<LayoutListItemViewModel>();

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

        RefreshLayouts();
        TryRestoreLastLayoutOnStartup();
    }

    public ObservableCollection<LayoutListItemViewModel> Layouts { get; }

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
                RaisePropertyChanged(nameof(CurrentLayoutName));
                RaisePropertyChanged(nameof(CanvasSubtitle));
                RaisePropertyChanged(nameof(SelectionLabel));
            }
        }
    }

    public string CurrentLayoutName
    {
        get => CurrentDocument.Name;
        set
        {
            var normalized = NormalizeLayoutName(value);
            if (normalized == CurrentDocument.Name)
            {
                return;
            }

            PushUndoState();
            _redoStack.Clear();
            CurrentDocument = CurrentDocument with { Name = normalized };
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

    public string? PreviewNodeId
    {
        get => _previewNodeId;
        set => SetProperty(ref _previewNodeId, value);
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

    public string DirtyLabel => IsDirty ? "未保存修改" : "已保存";

    public LayoutDocument SnapLayoutDocument => _activeSnapDocument;

    public LayoutDocument OverlaySnapDocument => _previewSnapDocument ?? _activeSnapDocument;

    public string ActiveSnapLayoutName => _activeSnapLayoutName;

    public string ActiveSnapLayoutId => _activeSnapLayoutId ?? string.Empty;

    public string SnapModifierKey => _appSettings.SnapModifierKey;

    public string MinimizeShortcut => _appSettings.MinimizeShortcut;

    public string CanvasSubtitle
    {
        get
        {
            var fileLabel = _currentLayoutId is null ? "当前是临时草稿" : $"当前文件：{_currentLayoutId}.json";
            return $"{fileLabel}  |  右键区域继续分割，拖动分割线实时调整比例。";
        }
    }

    public string SnapLayoutLabel
    {
        get
        {
            var idLabel = string.IsNullOrWhiteSpace(_activeSnapLayoutId) ? "未绑定到已保存布局" : $"{_activeSnapLayoutId}.json";
            return $"当前吸附布局：{_activeSnapLayoutName}  |  {idLabel}";
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

    public void UpdateSplitRatio(string splitNodeId, double ratio)
    {
        var result = _editorService.UpdateSplitRatio(CurrentDocument, splitNodeId, ratio);
        if (!result.Changed)
        {
            return;
        }

        ApplyEditorMutation(result.Document, result.SelectedNodeId);
    }

    public void UpdateSnapLayoutSplitRatio(string splitNodeId, double ratio)
    {
        var result = _editorService.UpdateSplitRatio(_activeSnapDocument, splitNodeId, ratio);
        if (!result.Changed)
        {
            return;
        }

        _activeSnapDocument = result.Document;
        RaisePropertyChanged(nameof(SnapLayoutDocument));

        if (!string.IsNullOrWhiteSpace(_activeSnapLayoutId)
            && string.Equals(_activeSnapLayoutId, _currentLayoutId, StringComparison.OrdinalIgnoreCase))
        {
            ApplyEditorMutation(result.Document, result.SelectedNodeId);
            return;
        }

        if (string.IsNullOrWhiteSpace(_activeSnapLayoutId))
        {
            ApplyEditorMutation(result.Document, result.SelectedNodeId);
        }
    }

    public bool PreviewSnapLayoutSplitRatio(string splitNodeId, double ratio)
    {
        var baseDocument = _previewSnapDocument ?? _activeSnapDocument;
        var result = _editorService.UpdateSplitRatio(baseDocument, splitNodeId, ratio);
        if (!result.Changed)
        {
            return false;
        }

        _previewSnapDocument = result.Document;
        RaisePropertyChanged(nameof(OverlaySnapDocument));
        return true;
    }

    public void ResetSnapLayoutPreview()
    {
        if (_previewSnapDocument is null)
        {
            return;
        }

        _previewSnapDocument = null;
        RaisePropertyChanged(nameof(OverlaySnapDocument));
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

        CurrentDocument = _editorService.CreateBlank($"布局 {Layouts.Count + 1}");
        _currentLayoutId = null;
        SelectedNodeId = CurrentDocument.Root.Id;
        _savedState = new PersistedLayoutState(CurrentDocument, _currentLayoutId);
        IsDirty = false;
        ResetHistory();
        RaisePropertyChanged(nameof(CanvasSubtitle));
    }

    private void SaveCurrentLayout()
    {
        var targetId = Slugify(CurrentDocument.Name);
        SaveToTarget(targetId, CurrentDocument.Name, treatAsSaveAs: false, notifyOnSuccess: true);
    }

    private void ApplyCurrentLayoutToWindows()
    {
        try
        {
            var owner = GetOwnerWindow();
            var excludedHandle = owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
            var workArea = SystemParameters.WorkArea;
            var rootBounds = new PaneRect(workArea.Left, workArea.Top, workArea.Width, workArea.Height);
            var result = _workspaceApplyService.Apply(SnapLayoutDocument, rootBounds, excludedHandle);

            if (result.AppliedWindowCount == 0)
            {
                ShowInfoMessage("没有找到可排列的桌面窗口。请先打开几个普通应用窗口再试。");
                return;
            }

            ShowInfoMessage($"已按当前布局排列 {result.AppliedWindowCount} 个窗口，剩余空白区域 {result.UnusedRegionCount} 个，未排入窗口 {result.SkippedWindowCount} 个。");
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"应用布局失败：{ex.Message}");
        }
    }

    private void SaveCurrentLayoutAs()
    {
        var enteredName = PromptForLayoutName("布局另存为", "请输入新布局名称。", CurrentDocument.Name);
        if (enteredName is null)
        {
            return;
        }

        var targetName = NormalizeLayoutName(enteredName);
        var targetId = Slugify(targetName);
        SaveToTarget(targetId, targetName, treatAsSaveAs: true, notifyOnSuccess: true);
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
            var document = _layoutRepository
                .LoadAsync(SelectedLayoutItem.Id, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CurrentDocument = document;
            _currentLayoutId = SelectedLayoutItem.Id;
            SelectedNodeId = CurrentDocument.Root.Id;
            _savedState = new PersistedLayoutState(CurrentDocument, _currentLayoutId);
            IsDirty = false;
            ResetHistory();
            SaveSessionState(_currentLayoutId, _activeSnapLayoutId);
            RaisePropertyChanged(nameof(CanvasSubtitle));
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

        var confirmed = ShowMessage($"确定删除布局“{SelectedLayoutItem.Name}”吗？", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _layoutRepository
                .DeleteAsync(SelectedLayoutItem.Id, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (_currentLayoutId == SelectedLayoutItem.Id)
            {
                CurrentDocument = _editorService.CreateBlank("起始布局");
                _currentLayoutId = null;
                SelectedNodeId = CurrentDocument.Root.Id;
                _savedState = new PersistedLayoutState(CurrentDocument, _currentLayoutId);
                IsDirty = false;
                ResetHistory();
                SaveSessionState(null, string.Equals(_activeSnapLayoutId, SelectedLayoutItem.Id, StringComparison.OrdinalIgnoreCase) ? null : _activeSnapLayoutId);
            }

            if (string.Equals(_activeSnapLayoutId, SelectedLayoutItem.Id, StringComparison.OrdinalIgnoreCase))
            {
                SetActiveSnapLayout(null, "当前编辑布局", CurrentDocument);
            }

            RefreshLayouts();
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

        var documentToSave = CurrentDocument with { Name = targetName };

        try
        {
            _layoutRepository
                .SaveAsync(targetId, documentToSave, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (willRenameCurrentFile)
            {
                _layoutRepository
                    .DeleteAsync(previousId!, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"保存布局失败：{ex.Message}");
            return false;
        }

        CurrentDocument = documentToSave;
        _currentLayoutId = targetId;
        _savedState = new PersistedLayoutState(CurrentDocument, _currentLayoutId);
        IsDirty = false;

        if (string.Equals(_activeSnapLayoutId, previousId, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(_activeSnapLayoutId) && ReferenceEquals(_activeSnapDocument, CurrentDocument)))
        {
            SetActiveSnapLayout(targetId, targetName, documentToSave);
        }

        SaveSessionState(_currentLayoutId, _activeSnapLayoutId);

        RefreshLayouts();
        SelectedLayoutItem = Layouts.FirstOrDefault(item => item.Id == _currentLayoutId);
        RaisePropertyChanged(nameof(CanvasSubtitle));

        if (notifyOnSuccess)
        {
            ShowInfoMessage($"布局“{targetName}”已保存。");
        }

        return true;
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Push(new EditorHistoryState(CurrentDocument, SelectedNodeId));
        RestoreHistoryState(_undoStack.Pop());
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(new EditorHistoryState(CurrentDocument, SelectedNodeId));
        RestoreHistoryState(_redoStack.Pop());
    }

    private void RefreshLayouts()
    {
        var items = _layoutRepository
            .ListAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        var selectedId = SelectedLayoutItem?.Id ?? _currentLayoutId;

        Layouts.Clear();

        foreach (var item in items)
        {
            Layouts.Add(new LayoutListItemViewModel
            {
                Id = item.Id,
                Name = item.Name,
                Description = $"已保存文件：{item.Id}.json"
            });
        }

        SelectedLayoutItem = Layouts.FirstOrDefault(item => item.Id == selectedId);
    }

    private void TryRestoreLastLayoutOnStartup()
    {
        var sessionState = _sessionStateRepository.Load();
        if (!string.IsNullOrWhiteSpace(sessionState.LastLayoutId))
        {
            var matchingLayout = Layouts.FirstOrDefault(item =>
                string.Equals(item.Id, sessionState.LastLayoutId, StringComparison.OrdinalIgnoreCase));

            if (matchingLayout is null)
            {
                SaveSessionState(null, sessionState.LastSnapLayoutId);
            }
            else
            {
                try
                {
                    var document = _layoutRepository
                        .LoadAsync(matchingLayout.Id, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();

                    CurrentDocument = document;
                    _currentLayoutId = matchingLayout.Id;
                    SelectedNodeId = CurrentDocument.Root.Id;
                    _savedState = new PersistedLayoutState(CurrentDocument, _currentLayoutId);
                    IsDirty = false;
                    ResetHistory();
                    SelectedLayoutItem = matchingLayout;
                    RaisePropertyChanged(nameof(CanvasSubtitle));
                }
                catch
                {
                    SaveSessionState(null, sessionState.LastSnapLayoutId);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(sessionState.LastSnapLayoutId))
        {
            SetActiveSnapLayout(_currentLayoutId, CurrentDocument.Name, CurrentDocument);
            return;
        }

        var snapLayout = Layouts.FirstOrDefault(item =>
            string.Equals(item.Id, sessionState.LastSnapLayoutId, StringComparison.OrdinalIgnoreCase));

        if (snapLayout is null)
        {
            SetActiveSnapLayout(_currentLayoutId, CurrentDocument.Name, CurrentDocument);
            SaveSessionState(_currentLayoutId, null);
            return;
        }

        try
        {
            var snapDocument = _layoutRepository
                .LoadAsync(snapLayout.Id, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            SetActiveSnapLayout(snapLayout.Id, snapDocument.Name, snapDocument);
        }
        catch
        {
            SetActiveSnapLayout(_currentLayoutId, CurrentDocument.Name, CurrentDocument);
            SaveSessionState(_currentLayoutId, null);
        }
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

        return SaveToTarget(Slugify(CurrentDocument.Name), CurrentDocument.Name, treatAsSaveAs: false, notifyOnSuccess: false);
    }

    private void ApplyEditorMutation(LayoutDocument document, string? selectedNodeId)
    {
        PushUndoState();
        _redoStack.Clear();
        CurrentDocument = document;
        SelectedNodeId = selectedNodeId;
        UpdateDirtyState();
        UpdateHistoryCommandStates();
    }

    private void RestoreHistoryState(EditorHistoryState state)
    {
        CurrentDocument = state.Document;
        SelectedNodeId = state.SelectedNodeId;
        UpdateDirtyState();
        UpdateHistoryCommandStates();
    }

    private void PushUndoState()
    {
        _undoStack.Push(new EditorHistoryState(CurrentDocument, SelectedNodeId));
    }

    private void ResetHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        UpdateHistoryCommandStates();
    }

    private void UpdateDirtyState()
    {
        IsDirty = !Equals(_savedState, new PersistedLayoutState(CurrentDocument, _currentLayoutId));
    }

    private void UpdateHistoryCommandStates()
    {
        _undoCommand.RaiseCanExecuteChanged();
        _redoCommand.RaiseCanExecuteChanged();
    }

    private void SaveSessionState(string? layoutId, string? snapLayoutId)
    {
        _sessionStateRepository.Save(new SessionState(layoutId, snapLayoutId));
    }

    private void SetActiveSnapLayout(string? layoutId, string layoutName, LayoutDocument document)
    {
        _activeSnapLayoutId = layoutId;
        _activeSnapLayoutName = layoutName;
        _activeSnapDocument = document;
        RaisePropertyChanged(nameof(ActiveSnapLayoutName));
        RaisePropertyChanged(nameof(ActiveSnapLayoutId));
        RaisePropertyChanged(nameof(SnapLayoutDocument));
        RaisePropertyChanged(nameof(SnapLayoutLabel));
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
            var document = _layoutRepository
                .LoadAsync(selectedItem.Id, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            SelectedLayoutItem = selectedItem;
            SetActiveSnapLayout(selectedItem.Id, document.Name, document);
            SaveSessionState(_currentLayoutId, _activeSnapLayoutId);

            if (notifyOnSuccess)
            {
                ShowInfoMessage($"当前吸附布局已切换为“{document.Name}”。");
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"设置吸附布局失败：{ex.Message}");
            return false;
        }
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

    private sealed record EditorHistoryState(LayoutDocument Document, string? SelectedNodeId);

    private sealed record PersistedLayoutState(LayoutDocument Document, string? LayoutId);
}
