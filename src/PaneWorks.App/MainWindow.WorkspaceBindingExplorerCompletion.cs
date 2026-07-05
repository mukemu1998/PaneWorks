using PaneWorks.App.Diagnostics;
using PaneWorks.Core.Models;
using PaneWorks.Infrastructure.Windows;

namespace PaneWorks.App;

public partial class MainWindow
{
    private void QueueExplorerFolderBindingCompletion(IReadOnlyList<SnappedWorkspaceWindowBindingRequest> requests)
    {
        foreach (var request in requests.Where(item => IsExplorerProcess(item.WindowInfo.ProcessName)))
        {
            _ = Task.Run(() =>
            {
                try
                {
                    return _workspaceApplyService.TryGetExplorerFolderPath(request.WindowInfo.Handle, out var folderPath)
                        ? NormalizeFolderPath(folderPath)
                        : string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }).ContinueWith(task =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    var folderPath = task.Result;
                    if (string.IsNullOrWhiteSpace(folderPath))
                    {
                        return;
                    }

                    var completedWindowInfo = request.WindowInfo with { ExplorerFolderPath = folderPath };
                    var binding = CreateWorkspaceWindowBinding(request.DisplayId, request.NodeId, completedWindowInfo, request.StackOrder);
                    if (ViewModel.TryUpsertWorkspaceWindowBindingPatch(
                            binding,
                            $"已补全 Explorer 文件夹绑定：{folderPath}"))
                    {
                        _snapWindowInfoCache[request.WindowInfo.Handle] = completedWindowInfo;
                        PaneWorksLog.Info($"Explorer folder binding completed: {folderPath}");
                    }
                });
            }, TaskScheduler.Default);
        }
    }

    private static WorkspaceWindowBinding CreateWorkspaceWindowBinding(
        string displayId,
        string nodeId,
        VisibleWindowInfo windowInfo,
        int stackOrder = 0)
    {
        var explorerFolderPath = NormalizeFolderPath(windowInfo.ExplorerFolderPath);
        var isExplorerFolder = IsExplorerProcess(windowInfo.ProcessName)
            && !string.IsNullOrWhiteSpace(explorerFolderPath);

        return new WorkspaceWindowBinding(
            displayId,
            nodeId,
            windowInfo.ProcessName,
            windowInfo.Title,
            windowInfo.ExecutablePath,
            string.Empty,
            isExplorerFolder ? explorerFolderPath : TryGetWorkingDirectory(windowInfo.ExecutablePath),
            isExplorerFolder ? "ExplorerFolder" : "Window",
            isExplorerFolder ? "FolderPath" : "Auto",
            isExplorerFolder ? explorerFolderPath : string.Empty,
            Math.Max(0, stackOrder));
    }
}
