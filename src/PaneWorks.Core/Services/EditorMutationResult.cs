namespace PaneWorks.Core.Services;

public sealed record EditorMutationResult<TDocument>(
    TDocument Document,
    string SelectedNodeId,
    bool Changed);

