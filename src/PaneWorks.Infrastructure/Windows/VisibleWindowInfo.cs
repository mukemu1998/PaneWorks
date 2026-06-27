namespace PaneWorks.Infrastructure.Windows;

public sealed record VisibleWindowInfo(
    IntPtr Handle,
    string ProcessName,
    string Title);
