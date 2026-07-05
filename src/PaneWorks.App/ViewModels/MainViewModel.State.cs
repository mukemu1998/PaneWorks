using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<LayoutListItemViewModel> Layouts { get; }

    public ObservableCollection<DisplayItemViewModel> Displays { get; }

    public ICommand NewLayoutCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

    public ICommand SaveLayoutCommand { get; }

    public ICommand SaveAsLayoutCommand { get; }

    public ICommand EditActiveSnapLayoutCommand { get; }

    public ICommand ExitLayoutEditModeCommand { get; }

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
                RaiseWindowBindingStatusChanged();
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
            RaisePropertyChanged(nameof(DisplayedLayoutName));
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
                RaiseWindowBindingStatusChanged();
            }
        }
    }

    public LayoutListItemViewModel? SelectedLayoutItem
    {
        get => _selectedLayoutItem;
        set
        {
            if (!SetProperty(ref _selectedLayoutItem, value))
            {
                return;
            }

            if (SetSelectedLayoutAsSnapLayoutCommand is RelayCommand snapCommand)
            {
                snapCommand.RaiseCanExecuteChanged();
            }

            _loadSelectedLayoutCommand.RaiseCanExecuteChanged();
            if (NewWorkspaceProfileCommand is RelayCommand createWorkspaceFromSelectedCommand)
            {
                createWorkspaceFromSelectedCommand.RaiseCanExecuteChanged();
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
            RaiseWindowBindingStatusChanged();

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

    public bool IsLayoutEditMode
    {
        get => _isLayoutEditMode;
        private set
        {
            if (!SetProperty(ref _isLayoutEditMode, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(IsDesktopOverlayVisible));
            RaisePropertyChanged(nameof(DesktopOverlayVisibility));
            RaisePropertyChanged(nameof(DisplayedLayoutName));
            RaisePropertyChanged(nameof(LayoutEditModeLabel));
            RaisePropertyChanged(nameof(LayoutEditModeHint));
            RaisePropertyChanged(nameof(IsLayoutNameEditable));
            RaisePropertyChanged(nameof(IsLayoutNameReadOnly));
            UpdateLayoutCommandStates();
        }
    }

    public bool IsWorkspaceBindingMode
    {
        get => _isWorkspaceBindingMode;
        private set
        {
            if (!SetProperty(ref _isWorkspaceBindingMode, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(IsDesktopOverlayVisible));
            RaisePropertyChanged(nameof(DesktopOverlayVisibility));
            RaisePropertyChanged(nameof(WorkspaceBindingModeLabel));
            RaisePropertyChanged(nameof(CanEditWorkspaceBindings));
            RaiseWindowBindingStatusChanged();
            UpdateWorkspaceBindingCommandStates();
        }
    }

    public bool IsDesktopOverlayVisible => IsLayoutEditMode || IsWorkspaceBindingMode;

    public Visibility DesktopOverlayVisibility => IsDesktopOverlayVisible ? Visibility.Visible : Visibility.Collapsed;

    public bool IsLayoutNameEditable => IsLayoutEditMode;

    public bool IsLayoutNameReadOnly => !IsLayoutNameEditable;

    public string DisplayedLayoutName
    {
        get => IsLayoutEditMode ? CurrentLayoutName : ActiveSnapLayoutName;
        set
        {
            if (IsLayoutEditMode)
            {
                CurrentLayoutName = value;
                RaisePropertyChanged();
            }
        }
    }

    public string LayoutEditModeLabel => IsLayoutEditMode ? "正在编辑分区" : "当前为选择模式";

    public string LayoutEditModeHint => IsLayoutEditMode
        ? "已进入分区编辑：可在桌面覆盖层右键创建分割线、拖动调整比例、右键删除分割。完成后保存或退出编辑。"
        : "主菜单默认只选择吸附布局；需要修改分割线时，先点击“新建分区”“编辑当前选择”或列表里的“打开编辑”，进入编辑后再操作桌面分区。";

    public string WorkspaceBindingModeLabel => IsWorkspaceBindingMode ? "正在选择绑定区域" : "未进入绑定模式";

    public bool CanEditWorkspaceBindings => IsWorkspaceBindingMode && IsWorkspaceProfileEnabled;

    public string ActiveSnapLayoutName => _activeSnapLayoutName;

    public string ActiveSnapLayoutId => _activeSnapLayoutId ?? string.Empty;

    public string SnapModifierKey => _appSettings.SnapModifierKey;

    public string RuntimeSessionModifierKey => _appSettings.RuntimeSessionModifierKey;

    public string MinimizeShortcut => _appSettings.MinimizeShortcut;

    public string CurrentDisplayName => SelectedDisplayItem?.Name ?? "主屏幕";

    public string CurrentDisplaySummary => SelectedDisplayItem?.Description ?? string.Empty;

    public string CanvasSubtitle
    {
        get
        {
            var fileLabel = _currentLayoutId is null ? "当前是临时多屏草稿" : $"当前分区文件：{_currentLayoutId}.json";
            return $"编辑屏幕：{CurrentDisplayName}  |  {fileLabel}  |  同一个分区布局文件会同时保存所有屏幕的分割结果。";
        }
    }

    public string StatusLine => string.IsNullOrWhiteSpace(_lastStatusMessage)
        ? CanvasSubtitle
        : $"{CanvasSubtitle}  |  最近操作：{_lastStatusMessage}";

    public string SnapLayoutLabel
    {
        get
        {
            var idLabel = string.IsNullOrWhiteSpace(_activeSnapLayoutId) ? "未绑定到已保存分区布局" : $"{_activeSnapLayoutId}.json";
            return $"当前分区布局：{_activeSnapLayoutName}  |  {idLabel}  |  会在所有屏幕统一生效";
        }
    }

    public string SelectedRegionBindingSummary
    {
        get => BuildSelectedRegionBindingSummary();
    }

    public string SelectedRegionBindingDescription
    {
        get => BuildSelectedRegionBindingDescription();
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
            var runtimeLabel = ShortcutGestureHelper.ToDisplayString(_appSettings.RuntimeSessionModifierKey, "Ctrl + Shift");
            var minimizeLabel = ShortcutGestureHelper.ToDisplayString(_appSettings.MinimizeShortcut, "Esc");
            return $"拖动目标窗口时按住：用户分区吸附 {snapLabel}  |  临时调整区吸附 {runtimeLabel}  |  托盘：{minimizeLabel}  |  开机自启：{startupLabel}";
        }
    }

    public bool TryGetSelectedLeafRegion(out string displayId, out string nodeId)
    {
        displayId = SelectedDisplayItem?.Id ?? GetPrimaryDisplayId();
        nodeId = SelectedNodeId ?? string.Empty;

        return !string.IsNullOrWhiteSpace(nodeId)
            && _queryService.IsLeaf(CurrentDocument, nodeId);
    }

    public bool TrySetSelectedRegionWindowBinding(
        string processName,
        string windowTitleSnapshot,
        string executablePath,
        string explorerFolderPath,
        out string message)
        => TrySetSelectedRegionWindowBindingCore(
            processName,
            windowTitleSnapshot,
            executablePath,
            explorerFolderPath,
            out message);

    public bool TrySetSelectedRegionWindowBinding(string processName, string windowTitleSnapshot, out string message)
        => TrySetSelectedRegionWindowBindingCore(
            processName,
            windowTitleSnapshot,
            string.Empty,
            string.Empty,
            out message);

    public bool TryClearSelectedRegionWindowBinding(out string message)
        => TryClearSelectedRegionWindowBindingCore(out message);

    public bool TryClose()
    {
        return ResolvePendingChanges("关闭 PaneWorks");
    }
}
