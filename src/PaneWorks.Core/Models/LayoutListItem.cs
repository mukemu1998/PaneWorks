namespace PaneWorks.Core.Models;

public sealed record LayoutListItem(
    string Id,
    string Name,
    string FilePath,
    DateTimeOffset LastModified);

