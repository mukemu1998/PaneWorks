namespace PaneWorks.Core.Models;

public sealed record WorkspaceLayoutDocument(
    int Version,
    string Name,
    Dictionary<string, LayoutDocument> DisplayLayouts,
    List<WorkspaceWindowBinding>? WindowBindings = null);
