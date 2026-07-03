namespace PaneWorks.Core.Models;

public sealed record WorkspaceProfileDocument(
    int Version,
    string Name,
    string LayoutId,
    List<WorkspaceWindowBinding>? WindowBindings = null);
