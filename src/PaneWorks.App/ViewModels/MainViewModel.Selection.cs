namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
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
            if (IsLayoutEditMode && !ReferenceEquals(_selectedLayoutItem, value))
            {
                return;
            }

            if (!SetProperty(ref _selectedLayoutItem, value))
            {
                return;
            }

            if (SetSelectedLayoutAsSnapLayoutCommand is RelayCommand snapCommand)
            {
                snapCommand.RaiseCanExecuteChanged();
            }

            _loadSelectedLayoutCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(CanOpenSelectedLayoutForEdit));
            if (NewWorkspaceProfileCommand is RelayCommand createWorkspaceFromSelectedCommand)
            {
                createWorkspaceFromSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void ClearSelectedLayout()
    {
        if (!IsLayoutEditMode)
        {
            SelectedLayoutItem = null;
        }
    }

    public void ClearSelectedWorkspaceProfile()
    {
        if (!IsWorkspaceBindingMode)
        {
            SelectedWorkspaceProfileItem = null;
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

    public bool TryGetSelectedLeafRegion(out string displayId, out string nodeId)
    {
        displayId = SelectedDisplayItem?.Id ?? GetPrimaryDisplayId();
        nodeId = SelectedNodeId ?? string.Empty;

        return !string.IsNullOrWhiteSpace(nodeId)
            && _queryService.IsLeaf(CurrentDocument, nodeId);
    }
}
