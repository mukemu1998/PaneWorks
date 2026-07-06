namespace PaneWorks.App.ViewModels;

public sealed class LayoutListItemViewModel
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public bool IsEmptyWorkspaceBinding { get; init; }

    public bool HasWorkspaceBinding { get; init; }

    public bool HasWorkspaceWarning { get; init; }
}
