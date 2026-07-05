using PaneWorks.Core.Models;

namespace PaneWorks.App;

public partial class MainWindow
{
    private static string GetBindingKey(WorkspaceWindowBinding binding)
    {
        return GetBindingKey(binding.DisplayId, binding.NodeId);
    }

    private static string GetBindingInstanceKey(WorkspaceWindowBinding binding)
    {
        return string.Join(
            "::",
            binding.DisplayId,
            binding.NodeId,
            binding.ProcessName,
            binding.WindowTitleSnapshot,
            binding.ExecutablePath,
            binding.LaunchTarget,
            binding.StackOrder.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string GetBindingKey(string displayId, string nodeId)
    {
        return $"{displayId}::{nodeId}";
    }

    private static bool IsExplorerFolderBinding(WorkspaceWindowBinding binding)
    {
        return string.Equals(binding.MatchKind, "ExplorerFolder", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(binding.LaunchTarget);
    }
}
