using System.IO;
using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private static bool IsExplorerFolderBinding(WorkspaceWindowBinding binding)
    {
        return string.Equals(binding.MatchKind, "ExplorerFolder", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(binding.LaunchTarget);
    }

    private static bool IsExplorerProcess(string processName)
    {
        return string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, "explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFolderPath(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch
        {
        }

        return TrimTrailingDirectorySeparators(normalized);
    }

    private static string TrimTrailingDirectorySeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(root)
            && string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrWhiteSpace(trimmed) ? path : trimmed;
    }
}
