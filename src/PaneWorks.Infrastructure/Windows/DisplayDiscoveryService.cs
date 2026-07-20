using System.Drawing;
using System.Windows.Forms;
using PaneWorks.Core.Models;

namespace PaneWorks.Infrastructure.Windows;

public sealed class DisplayDiscoveryService
{
    private readonly DisplayIdentityResolver _identityResolver = new();

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var screens = Screen.AllScreens
            .OrderBy(screen => screen.Bounds.X)
            .ThenBy(screen => screen.Bounds.Y)
            .ToList();

        var displays = new List<DisplayInfo>(screens.Count);
        var secondaryIndex = 1;

        for (var index = 0; index < screens.Count; index++)
        {
            var screen = screens[index];
            var name = screen.Primary ? "主屏幕" : $"副屏幕 {secondaryIndex++}";
            displays.Add(new DisplayInfo(
                GetId(screen),
                screen.DeviceName,
                name,
                ToPaneRect(screen.Bounds),
                ToPaneRect(screen.WorkingArea),
                screen.Primary,
                GetOrientation(screen.Bounds)));
        }

        return displays;
    }

    public DisplayInfo? TryGetDisplayById(string? displayId)
    {
        if (string.IsNullOrWhiteSpace(displayId))
        {
            return null;
        }

        return GetDisplays().FirstOrDefault(display =>
            string.Equals(display.Id, displayId, StringComparison.OrdinalIgnoreCase));
    }

    public DisplayInfo GetPrimaryDisplay()
    {
        return GetDisplays().FirstOrDefault(display => display.IsPrimary)
            ?? GetDisplays().First();
    }

    public DisplayInfo GetDisplayFromWindow(IntPtr windowHandle)
    {
        var screen = windowHandle == IntPtr.Zero
            ? Screen.PrimaryScreen ?? Screen.AllScreens.First()
            : Screen.FromHandle(windowHandle);
        return CreateDisplayInfo(screen);
    }

    public DisplayInfo GetDisplayFromPoint(int x, int y)
    {
        var screen = Screen.FromPoint(new Point(x, y));
        return CreateDisplayInfo(screen);
    }

    public PaneRect GetVirtualDesktopBounds()
    {
        return ToPaneRect(SystemInformation.VirtualScreen);
    }

    private DisplayInfo CreateDisplayInfo(Screen screen)
    {
        var existing = GetDisplays().FirstOrDefault(display =>
            string.Equals(display.Id, GetId(screen), StringComparison.OrdinalIgnoreCase));

        return existing ?? new DisplayInfo(
            GetId(screen),
            screen.DeviceName,
            screen.Primary ? "主屏幕" : screen.DeviceName,
            ToPaneRect(screen.Bounds),
            ToPaneRect(screen.WorkingArea),
            screen.Primary,
            GetOrientation(screen.Bounds));
    }

    private string GetId(Screen screen)
    {
        return _identityResolver.GetPhysicalId(screen);
    }

    private static WorkspaceDisplayOrientation GetOrientation(Rectangle bounds)
    {
        return bounds.Height > bounds.Width
            ? WorkspaceDisplayOrientation.Portrait
            : WorkspaceDisplayOrientation.Landscape;
    }

    private static PaneRect ToPaneRect(Rectangle rectangle)
    {
        return new PaneRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }
}
