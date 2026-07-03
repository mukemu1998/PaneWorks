namespace PaneWorks.Core.Models;

public sealed record WorkspaceWindowBinding(
    string DisplayId,
    string NodeId,
    string ProcessName,
    string WindowTitleSnapshot,
    string ExecutablePath = "",
    string LaunchArguments = "",
    string WorkingDirectory = "",
    string MatchKind = "Window",
    string MatchMode = "Auto",
    string LaunchTarget = "",
    int StackOrder = 0);
