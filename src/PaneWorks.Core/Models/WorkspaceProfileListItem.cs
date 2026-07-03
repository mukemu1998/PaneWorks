namespace PaneWorks.Core.Models;

public sealed record WorkspaceProfileListItem(
    string Id,
    string Name,
    string LayoutId,
    int BindingCount,
    string FilePath,
    DateTimeOffset LastModified);
