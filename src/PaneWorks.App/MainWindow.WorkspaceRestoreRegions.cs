using PaneWorks.Core.Models;

namespace PaneWorks.App;

public partial class MainWindow
{
    private BindingRegionResolveResult ResolveBindingRegion(
        WorkspaceWindowBinding binding,
        Dictionary<string, IReadOnlyDictionary<string, ComputedRegion>> geometryByDisplay)
    {
        if (!ViewModel.TryGetDisplayById(binding.DisplayId, out var display))
        {
            return BindingRegionResolveResult.Failed(BindingRegionResolveFailure.DisplayMissing);
        }

        if (!geometryByDisplay.TryGetValue(display.Id, out var regionsById))
        {
            var geometry = _geometryCalculator.Compute(
                ViewModel.GetSnapLayoutDocumentForDisplay(display.Id),
                GetSnapTargetStageBounds(display),
                SnapTargetSplitterThickness);
            regionsById = geometry.Regions.ToDictionary(item => item.NodeId, StringComparer.Ordinal);
            geometryByDisplay[display.Id] = regionsById;
        }

        if (regionsById is null)
        {
            return BindingRegionResolveResult.Failed(BindingRegionResolveFailure.RegionMissing);
        }

        if (!regionsById.TryGetValue(binding.NodeId, out var foundRegion))
        {
            return BindingRegionResolveResult.Failed(BindingRegionResolveFailure.RegionMissing);
        }

        return BindingRegionResolveResult.Found(foundRegion);
    }

    private bool TryGetBindingRegion(
        WorkspaceWindowBinding binding,
        Dictionary<string, IReadOnlyDictionary<string, ComputedRegion>> geometryByDisplay,
        out ComputedRegion region)
    {
        var result = ResolveBindingRegion(binding, geometryByDisplay);
        region = result.Region!;
        return result.Success && result.Region is not null;
    }

    private string ResolveBindingRegionFailureMessage(WorkspaceWindowBinding binding, BindingRegionResolveFailure failure)
    {
        return failure == BindingRegionResolveFailure.DisplayMissing
            ? $"目标屏幕不存在或屏幕标识已变化：{binding.DisplayId}"
            : $"找不到绑定区域：{binding.NodeId}。可能是关联分区布局已重新编辑。";
    }

    private static string FormatWorkspaceBindingTarget(WorkspaceWindowBinding binding)
    {
        if (IsExplorerFolderBinding(binding) && !string.IsNullOrWhiteSpace(binding.LaunchTarget))
        {
            return $"Explorer 文件夹 {binding.LaunchTarget}";
        }

        var processName = string.IsNullOrWhiteSpace(binding.ProcessName)
            ? "未知窗口"
            : $"{binding.ProcessName}.exe";
        return string.IsNullOrWhiteSpace(binding.WindowTitleSnapshot)
            ? processName
            : $"{processName}（{binding.WindowTitleSnapshot}）";
    }
}
