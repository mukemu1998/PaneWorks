using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaneWorks.Core.Models;
using DrawingIcon = System.Drawing.Icon;

namespace PaneWorks.App.ViewModels;

public sealed class WorkspaceBindingIconItemViewModel
{
    private WorkspaceBindingIconItemViewModel(WorkspaceWindowBinding binding, ImageSource? iconSource)
    {
        ProcessName = string.IsNullOrWhiteSpace(binding.ProcessName) ? "未命名窗口" : binding.ProcessName;
        DisplayName = string.IsNullOrWhiteSpace(binding.WindowTitleSnapshot)
            ? ProcessName
            : binding.WindowTitleSnapshot;
        IconSource = iconSource;
        FallbackGlyph = ProcessName[..1].ToUpperInvariant();
    }

    public string ProcessName { get; }

    public string DisplayName { get; }

    public ImageSource? IconSource { get; }

    public string FallbackGlyph { get; }

    public static WorkspaceBindingIconItemViewModel Create(WorkspaceWindowBinding binding)
        => new(binding, TryLoadIcon(binding.ExecutablePath));

    private static ImageSource? TryLoadIcon(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        try
        {
            using var icon = DrawingIcon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }
}
