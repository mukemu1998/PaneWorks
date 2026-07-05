using PaneWorks.Core.Models;

namespace PaneWorks.Core.Services;

public static class WorkspaceWindowBindingNormalizer
{
    public static List<WorkspaceWindowBinding> NormalizeMany(IEnumerable<WorkspaceWindowBinding>? bindings)
    {
        return bindings?
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.DisplayId)
                && !string.IsNullOrWhiteSpace(item.NodeId)
                && !string.IsNullOrWhiteSpace(item.ProcessName))
            .Select(Normalize)
            .ToList()
            ?? new List<WorkspaceWindowBinding>();
    }

    public static WorkspaceWindowBinding Normalize(WorkspaceWindowBinding binding)
    {
        var processName = binding.ProcessName.Trim();
        var executablePath = binding.ExecutablePath?.Trim() ?? string.Empty;
        var launchTarget = binding.LaunchTarget?.Trim() ?? string.Empty;
        var workingDirectory = binding.WorkingDirectory?.Trim() ?? string.Empty;
        var matchKind = NormalizeToken(binding.MatchKind, "Window");
        var matchMode = NormalizeToken(binding.MatchMode, "Auto");

        if (IsExplorerProcess(processName))
        {
            var explorerFolderPath = ResolveExplorerFolderPathCandidate(
                executablePath,
                launchTarget,
                workingDirectory,
                matchKind);

            if (!string.IsNullOrWhiteSpace(explorerFolderPath))
            {
                launchTarget = explorerFolderPath;
                workingDirectory = explorerFolderPath;
                matchKind = "ExplorerFolder";
                matchMode = "FolderPath";
            }
        }

        return binding with
        {
            DisplayId = binding.DisplayId.Trim(),
            NodeId = binding.NodeId.Trim(),
            ProcessName = processName,
            WindowTitleSnapshot = binding.WindowTitleSnapshot?.Trim() ?? string.Empty,
            ExecutablePath = executablePath,
            LaunchArguments = binding.LaunchArguments?.Trim() ?? string.Empty,
            WorkingDirectory = workingDirectory,
            MatchKind = matchKind,
            MatchMode = matchMode,
            LaunchTarget = launchTarget,
            StackOrder = Math.Max(0, binding.StackOrder)
        };
    }

    private static string ResolveExplorerFolderPathCandidate(
        string executablePath,
        string launchTarget,
        string workingDirectory,
        string matchKind)
    {
        var executableDirectory = NormalizeFolderPath(TryGetDirectoryName(executablePath));
        var candidates = new[]
        {
            launchTarget,
            string.Equals(matchKind, "ExplorerFolder", StringComparison.OrdinalIgnoreCase) ? workingDirectory : string.Empty,
            workingDirectory
        };

        foreach (var candidate in candidates)
        {
            var normalized = NormalizeFolderPath(candidate);
            if (string.IsNullOrWhiteSpace(normalized)
                || string.Equals(normalized, executableDirectory, StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(normalized))
            {
                continue;
            }

            return normalized;
        }

        return string.Empty;
    }

    private static string NormalizeToken(string? value, string fallback)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static bool IsExplorerProcess(string processName)
    {
        return string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, "explorer.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string TryGetDirectoryName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetDirectoryName(path) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
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
