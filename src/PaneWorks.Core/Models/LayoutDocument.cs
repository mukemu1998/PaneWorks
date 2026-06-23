namespace PaneWorks.Core.Models;

public sealed record LayoutDocument(
    int Version,
    string Name,
    LayoutNode Root);

