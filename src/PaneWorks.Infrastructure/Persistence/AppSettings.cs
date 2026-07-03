namespace PaneWorks.Infrastructure.Persistence;

public sealed record AppSettings(
    string SnapModifierKey,
    string RuntimeSessionModifierKey,
    string MinimizeShortcut,
    bool LaunchAtStartup)
{
    public static AppSettings Default { get; } = new("Shift", "Ctrl + Shift", "Escape", false);
}
