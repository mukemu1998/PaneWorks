using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using PaneWorks.Core.Abstractions;
using PaneWorks.Core.Models;
using PaneWorks.Core.Services;
using PaneWorks.Infrastructure.Persistence;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const string DefaultBlankWorkspaceName = "起始空白布局";
    private readonly LayoutEditorService _editorService = new();
    private readonly LayoutTreeQueryService _queryService = new();
    private readonly DisplayDiscoveryService _displayDiscoveryService = new();
    private readonly ILayoutRepository _layoutRepository;
    private readonly JsonAppSettingsRepository _appSettingsRepository;
    private readonly JsonSessionStateRepository _sessionStateRepository;
    private readonly StartupRegistrationService _startupRegistrationService = new();
    private readonly RelayCommand _newLayoutCommand;
    private readonly RelayCommand _undoCommand;
    private readonly RelayCommand _redoCommand;
    private readonly RelayCommand _saveLayoutCommand;
    private readonly RelayCommand _saveAsLayoutCommand;
    private readonly RelayCommand _editActiveSnapLayoutCommand;
    private readonly RelayCommand _loadSelectedLayoutCommand;
    private readonly RelayCommand _exitLayoutEditModeCommand;
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
    private string _pendingLayoutChangeDescription = string.Empty;
    private bool _suppressDisplaySelectionChange;
    private bool _isSavingLayout;
    private bool _isLayoutEditMode;
    private bool _isWorkspaceBindingMode;
    private string _lastStatusMessage = string.Empty;

    public MainViewModel()
    {
        _layoutRepository = new JsonLayoutRepository(LayoutStoragePaths.GetDefaultLayoutsDirectory());
        _workspaceProfileRepository = new JsonWorkspaceProfileRepository(
            LayoutStoragePaths.GetDefaultWorkspaceProfilesDirectory(),
            LayoutStoragePaths.GetDefaultLayoutsDirectory());
        _appSettingsRepository = new JsonAppSettingsRepository(LayoutStoragePaths.GetDefaultAppSettingsFilePath());
        _sessionStateRepository = new JsonSessionStateRepository(LayoutStoragePaths.GetDefaultSessionStateFilePath());
        _appSettings = LoadAppSettings();

        Layouts = new ObservableCollection<LayoutListItemViewModel>();
        WorkspaceProfiles = new ObservableCollection<LayoutListItemViewModel>();
        Displays = new ObservableCollection<DisplayItemViewModel>();

        RefreshDisplays();

        _currentWorkspaceDocument = CreateWorkspaceDocument(DefaultBlankWorkspaceName);
        _activeSnapWorkspaceDocument = _currentWorkspaceDocument;
        _currentDocument = GetDisplayLayout(_currentWorkspaceDocument, GetPrimaryDisplayId());
        _activeSnapLayoutName = "当前编辑布局";
        _selectedNodeId = _currentDocument.Root.Id;
        _savedState = new PersistedWorkspaceState(_currentWorkspaceDocument, _currentLayoutId);

        _undoCommand = new RelayCommand(Undo, () => IsLayoutEditMode && _undoStack.Count > 0);
        _redoCommand = new RelayCommand(Redo, () => IsLayoutEditMode && _redoStack.Count > 0);

        _newLayoutCommand = new RelayCommand(CreateNewLayout);
        UndoCommand = _undoCommand;
        RedoCommand = _redoCommand;
        _saveLayoutCommand = new RelayCommand(SaveCurrentLayout, () => IsLayoutEditMode);
        _saveAsLayoutCommand = new RelayCommand(SaveCurrentLayoutAs, () => IsLayoutEditMode);
        _editActiveSnapLayoutCommand = new RelayCommand(EditActiveSnapLayout);
        _loadSelectedLayoutCommand = new RelayCommand(LoadSelectedLayout, () => CanOpenSelectedLayoutForEdit);
        _exitLayoutEditModeCommand = new RelayCommand(ExitLayoutEditMode, () => IsLayoutEditMode);
        NewLayoutCommand = _newLayoutCommand;
        SaveLayoutCommand = _saveLayoutCommand;
        SaveAsLayoutCommand = _saveAsLayoutCommand;
        EditActiveSnapLayoutCommand = _editActiveSnapLayoutCommand;
        LoadSelectedLayoutCommand = _loadSelectedLayoutCommand;
        ExitLayoutEditModeCommand = _exitLayoutEditModeCommand;
        SetSelectedLayoutAsSnapLayoutCommand = new RelayCommand(SetSelectedLayoutAsSnapLayout, () => SelectedLayoutItem is not null);
        DeleteSelectedLayoutCommand = new RelayCommand(DeleteSelectedLayout);
        SplitHorizontalCommand = new RelayCommand(() => SplitLeafById(SelectedNodeId, SplitDirection.Horizontal), () => IsLayoutEditMode);
        SplitVerticalCommand = new RelayCommand(() => SplitLeafById(SelectedNodeId, SplitDirection.Vertical), () => IsLayoutEditMode);
        DeleteSelectedSplitCommand = new RelayCommand(() => DeleteContainingSplit(SelectedNodeId), () => IsLayoutEditMode);
        InitializeWorkspaceProfileCommands();

        RefreshDisplays();
        RefreshLayouts();
        RefreshWorkspaceProfiles();
        TryRestoreLastLayoutOnStartup();
    }

}
