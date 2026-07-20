using System.Collections.ObjectModel;
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
            UpdateDirtyState($"已将分区布局名称改为“{normalized}”");
            UpdateHistoryCommandStates();
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                if (!value)
                {
                    _pendingLayoutChangeDescription = string.Empty;
                }

                RaisePropertyChanged(nameof(DirtyLabel));
            }
        }
    }

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
            RaisePropertyChanged(nameof(IsLayoutListSelectionEnabled));
            RaisePropertyChanged(nameof(CanOpenSelectedLayoutForEdit));
            UpdateLayoutCommandStates();
        }
    }

    public bool IsLayoutListSelectionEnabled => !IsLayoutEditMode;

    public bool CanOpenSelectedLayoutForEdit => !IsLayoutEditMode && SelectedLayoutItem is not null;

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
            RaisePropertyChanged(nameof(IsWorkspaceProfileListSelectionEnabled));
            RaisePropertyChanged(nameof(CanEnterWorkspaceBindingMode));
            RaisePropertyChanged(nameof(CanManageSelectedWorkspaceProfile));
            RaisePropertyChanged(nameof(CanSaveWorkspaceProfileChanges));
            RaiseWindowBindingStatusChanged();
            UpdateWorkspaceBindingCommandStates();
        }
    }

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
        return !IsLayoutEditMode || ResolvePendingChanges("关闭 PaneWorks");
    }
}
