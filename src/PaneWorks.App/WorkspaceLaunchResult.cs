namespace PaneWorks.App;

internal sealed record WorkspaceLaunchResult(
    IReadOnlyList<PaneWorks.Core.Models.WorkspaceWindowBinding> StartedBindings,
    IReadOnlyList<WorkspaceRestoreIssue> SkippedIssues,
    IReadOnlyList<WorkspaceRestoreIssue> FailedIssues)
{
    public int StartedCount => StartedBindings.Count;

    public int SkippedCount => SkippedIssues.Count;

    public int FailedCount => FailedIssues.Count;
}
