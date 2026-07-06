using PaneWorks.App.Controls;
using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    public void SelectNode(string nodeId)
    {
        SelectedNodeId = nodeId;
    }

    public void UpdateSplitRatio(string splitNodeId, double ratio)
    {
        if (!IsLayoutEditMode)
        {
            return;
        }

        var result = _editorService.UpdateSplitRatio(CurrentDocument, splitNodeId, ratio);
        if (!result.Changed)
        {
            return;
        }

        ApplyEditorMutation(result.Document, result.SelectedNodeId);
    }

    public void HandleCanvasContextAction(CanvasContextAction action, string targetNodeId)
    {
        if (!IsLayoutEditMode)
        {
            SelectNode(targetNodeId);
            return;
        }

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

    private void SplitLeafById(string? nodeId, SplitDirection direction)
    {
        if (!IsLayoutEditMode)
        {
            return;
        }

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
        if (!IsLayoutEditMode)
        {
            return;
        }

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
        if (!IsLayoutEditMode)
        {
            return;
        }

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
}
