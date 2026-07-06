using Drawing = System.Drawing;

namespace PaneWorks.App;

public partial class App
{
    private static Drawing.Icon LoadNotifyIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(processPath);
                if (icon is not null)
                {
                    return (Drawing.Icon)icon.Clone();
                }
            }
        }
        catch
        {
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }
}
