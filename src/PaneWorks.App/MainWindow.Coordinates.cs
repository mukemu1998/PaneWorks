using System.Windows;
using System.Windows.Media;
using PaneWorks.Core.Models;
using WpfPoint = System.Windows.Point;

namespace PaneWorks.App;

public partial class MainWindow
{
    private PaneRect DeviceRectToDipRect(PaneRect rect)
    {
        var transform = GetTransformFromDevice();
        var topLeft = transform.Transform(new WpfPoint(rect.X, rect.Y));
        var bottomRight = transform.Transform(new WpfPoint(rect.X + rect.Width, rect.Y + rect.Height));
        return new PaneRect(
            topLeft.X,
            topLeft.Y,
            Math.Max(0, bottomRight.X - topLeft.X),
            Math.Max(0, bottomRight.Y - topLeft.Y));
    }

    private WpfPoint DevicePointToDipPoint(WpfPoint point)
    {
        return GetTransformFromDevice().Transform(point);
    }

    private Matrix GetTransformFromDevice()
    {
        return PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
    }
}
