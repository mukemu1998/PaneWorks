using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
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

    private void UpdateLayoutCommandStates()
    {
        _saveLayoutCommand.RaiseCanExecuteChanged();
        _saveAsLayoutCommand.RaiseCanExecuteChanged();
        _exitLayoutEditModeCommand.RaiseCanExecuteChanged();
        if (SplitHorizontalCommand is RelayCommand splitHorizontalCommand)
        {
            splitHorizontalCommand.RaiseCanExecuteChanged();
        }

        if (SplitVerticalCommand is RelayCommand splitVerticalCommand)
        {
            splitVerticalCommand.RaiseCanExecuteChanged();
        }

        if (DeleteSelectedSplitCommand is RelayCommand deleteSplitCommand)
        {
            deleteSplitCommand.RaiseCanExecuteChanged();
        }

        UpdateHistoryCommandStates();
    }

    private sealed record EditorHistoryState(WorkspaceLayoutDocument WorkspaceDocument, string? SelectedNodeId);
}
