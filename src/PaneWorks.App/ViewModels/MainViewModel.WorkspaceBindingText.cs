namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    public string BuildSelectedRegionBindingSummary()
    {
        if (!IsWorkspaceProfileEnabled || _activeWorkspaceProfileDocument is null)
        {
            return "当前还没有进入工作区绑定模式。";
        }

        if (!IsWorkspaceBindingMode)
        {
            return "先在左侧选择工作区方案，再点击“编辑绑定”。";
        }

        if (!IsCurrentEditorUsingActiveWorkspaceLayout())
        {
            return $"当前工作区方案“{_activeWorkspaceProfileName}”已启用，但当前编辑的不是它关联的分区布局。";
        }

        if (!TryGetSelectedLeafRegion(out var displayId, out var nodeId))
        {
            return "当前还没有选中可绑定的区域。";
        }

        var regionBindings = GetWindowBindings(_activeWorkspaceProfileDocument, displayId, nodeId);
        var binding = regionBindings.OrderBy(item => item.StackOrder).LastOrDefault();
        if (binding is not null && regionBindings.Count > 1)
        {
            return $"当前区域绑定窗口组：{regionBindings.Count} 个窗口，顶层为 {binding.ProcessName}.exe。";
        }

        if (binding is not null && IsExplorerFolderBinding(binding))
        {
            return $"当前区域绑定文件夹：{binding.LaunchTarget}";
        }

        return binding is null
            ? "当前区域还没有窗口绑定。"
            : string.IsNullOrWhiteSpace(binding.WindowTitleSnapshot)
                ? $"当前区域绑定：{binding.ProcessName}.exe"
                : $"当前区域绑定：{binding.ProcessName}.exe  |  {binding.WindowTitleSnapshot}";
    }

    public string BuildSelectedRegionBindingDescription()
    {
        if (!IsWorkspaceProfileEnabled || _activeWorkspaceProfileDocument is null)
        {
            return "工作区只负责窗口绑定。先从当前吸附或选中的保存分区创建方案，再进入绑定模式。";
        }

        if (!IsWorkspaceBindingMode)
        {
            return "默认不显示桌面绑定覆盖层。点击“编辑绑定”后可选择区域、手动绑定或一键绑定已吸附窗口。";
        }

        if (!IsCurrentEditorUsingActiveWorkspaceLayout())
        {
            return $"请先从左侧分区布局列表里打开“{ResolveLayoutDisplayName(_activeWorkspaceProfileDocument.LayoutId)}”，再编辑这套工作区方案的区域绑定。";
        }

        if (!TryGetSelectedLeafRegion(out var displayId, out var nodeId))
        {
            return "手动绑定需要先点击一个区域；如果窗口已经吸附到各区域，可直接使用一键绑定。";
        }

        var regionBindings = GetWindowBindings(_activeWorkspaceProfileDocument, displayId, nodeId);
        var binding = regionBindings.OrderBy(item => item.StackOrder).LastOrDefault();
        if (binding is null)
        {
            return "当前区域还没有绑定窗口。绑定后，手动切换这套工作区方案时就能按规则恢复窗口。";
        }

        if (regionBindings.Count > 1)
        {
            return $"当前区域是窗口组，共 {regionBindings.Count} 个窗口；恢复工作区时会全部吸附到这里，并让 {binding.ProcessName}.exe 保持在最顶层。";
        }

        if (IsExplorerFolderBinding(binding))
        {
            return $"匹配方式：Explorer 文件夹路径优先。路径：{binding.LaunchTarget}";
        }

        if (!string.IsNullOrWhiteSpace(binding.ExecutablePath))
        {
            return $"匹配方式：优先按程序路径，其次按标题。路径：{binding.ExecutablePath}";
        }

        return string.IsNullOrWhiteSpace(binding.WindowTitleSnapshot)
            ? $"匹配方式：按 {binding.ProcessName}.exe 处理。"
            : $"匹配方式：按 {binding.ProcessName}.exe + 标题快照。标题：{binding.WindowTitleSnapshot}";
    }
}
