namespace PaneWorks.Infrastructure.Persistence;

public sealed record AppSettings(string SnapModifierKey, string MinimizeShortcut, bool LaunchAtStartup)
{
    public static AppSettings Default { get; } = new("Shift", "Escape", false);
}
