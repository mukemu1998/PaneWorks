using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool TrySetSelectedRegionWindowBindingCore(
        string processName,
        string windowTitleSnapshot,
        string executablePath,
        string explorerFolderPath,
        out string message)
    {
        message = string.Empty;
        if (!CanEditWorkspaceBindings || _activeWorkspaceProfileDocument is null)
        {
            message = "请先选中工作区方案并点击“编辑绑定”，再给区域绑定窗口。";
            return false;
        }

        if (!IsCurrentEditorUsingActiveWorkspaceLayout())
        {
            message = $"请先打开工作区方案“{_activeWorkspaceProfileName}”关联的分区布局，再编辑区域绑定。";
            return false;
        }

        if (!TryGetSelectedLeafRegion(out var displayId, out var nodeId))
        {
            message = "请先选中一个区域，再给它绑定窗口。";
            return false;
        }

        var normalizedProcessName = processName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedProcessName))
        {
            message = "无效的窗口进程名，无法保存绑定。";
            return false;
        }

        var normalizedExplorerFolderPath = NormalizeFolderPath(explorerFolderPath);
        var isExplorerFolder = IsExplorerProcess(normalizedProcessName)
            && !string.IsNullOrWhiteSpace(normalizedExplorerFolderPath);

        _activeWorkspaceProfileDocument = ReplaceWindowBindingsForRegion(
            _activeWorkspaceProfileDocument,
            new WorkspaceWindowBinding(
                displayId,
                nodeId,
                normalizedProcessName,
                windowTitleSnapshot.Trim(),
                executablePath.Trim(),
                string.Empty,
                isExplorerFolder ? normalizedExplorerFolderPath : string.Empty,
                isExplorerFolder ? "ExplorerFolder" : "Window",
                isExplorerFolder ? "FolderPath" : "Auto",
                isExplorerFolder ? normalizedExplorerFolderPath : string.Empty));

        if (!TrySaveActiveWorkspaceProfileDocument(out var saveMessage))
        {
            message = saveMessage;
            return false;
        }

        RaiseWindowBindingStatusChanged();
        message = isExplorerFolder
            ? $"当前区域已绑定并保存到文件夹：{normalizedExplorerFolderPath}"
            : $"当前区域已绑定并保存到 {normalizedProcessName}.exe。";
        return true;
    }

}
