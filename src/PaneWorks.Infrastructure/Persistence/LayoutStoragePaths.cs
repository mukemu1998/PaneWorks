namespace PaneWorks.Infrastructure.Persistence;

public static class LayoutStoragePaths
{
    public static string GetDefaultAppSettingsFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "PaneWorks", "app-settings.json");
    }

    public static string GetDefaultLayoutsDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "PaneWorks", "Layouts");
    }

    public static string GetDefaultSessionStateFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "PaneWorks", "session-state.json");
    }
}
