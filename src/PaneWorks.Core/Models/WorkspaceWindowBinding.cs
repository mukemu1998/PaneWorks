namespace PaneWorks.Core.Models;

public sealed record WorkspaceWindowBinding(
    string DisplayId,
    string NodeId,
    string ProcessName,
    string WindowTitleSnapshot);
