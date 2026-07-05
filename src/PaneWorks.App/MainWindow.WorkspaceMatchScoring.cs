using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private static int ScoreWindowBinding(WorkspaceWindowBinding binding, VisibleWindowInfo window)
    {
        if (IsExplorerFolderBinding(binding))
        {
            var targetFolderPath = NormalizeFolderPath(binding.LaunchTarget);
            var windowFolderPath = NormalizeFolderPath(window.ExplorerFolderPath);
            if (string.IsNullOrWhiteSpace(targetFolderPath)
                || string.IsNullOrWhiteSpace(windowFolderPath)
                || !string.Equals(targetFolderPath, windowFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            return 200 + (IsExplorerProcess(window.ProcessName) ? 20 : 0);
        }

        if (IsExplorerProcess(binding.ProcessName) || IsExplorerProcess(window.ProcessName))
        {
            if (!IsExplorerProcess(binding.ProcessName) || !IsExplorerProcess(window.ProcessName))
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(binding.WindowTitleSnapshot)
                || string.IsNullOrWhiteSpace(window.Title)
                || !string.Equals(binding.WindowTitleSnapshot, window.Title, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            return 60;
        }

        var score = 0;
        if (!string.IsNullOrWhiteSpace(binding.ExecutablePath)
            && !string.IsNullOrWhiteSpace(window.ExecutablePath)
            && string.Equals(binding.ExecutablePath, window.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (string.Equals(binding.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (score == 0)
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(binding.WindowTitleSnapshot))
        {
            return score;
        }

        if (string.Equals(binding.WindowTitleSnapshot, window.Title, StringComparison.OrdinalIgnoreCase))
        {
            return score + 30;
        }

        if (window.Title.Contains(binding.WindowTitleSnapshot, StringComparison.OrdinalIgnoreCase)
            || binding.WindowTitleSnapshot.Contains(window.Title, StringComparison.OrdinalIgnoreCase))
        {
            return score + 20;
        }

        return score;
    }
}
