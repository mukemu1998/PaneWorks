using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool TryClearSelectedRegionWindowBindingCore(out string message)
    {
        message = string.Empty;
        if (!CanEditWorkspaceBindings
            || _activeWorkspaceProfileDocument is null
            || string.IsNullOrWhiteSpace(_activeWorkspaceProfileId))
        {
            message = "请先选中工作区方案并点击“编辑绑定”，再清除窗口绑定。";
            SetStatusMessage(message);
            return false;
        }

        if (!IsCurrentEditorUsingActiveWorkspaceLayout())
        {
            message = $"请先打开工作区方案“{_activeWorkspaceProfileName}”关联的分区布局，再编辑区域绑定。";
            SetStatusMessage(message);
            return false;
        }

        if (!TryGetSelectedLeafRegion(out var displayId, out var nodeId))
        {
            message = "请先选中一个区域，再清除绑定。";
            SetStatusMessage(message);
            return false;
        }

        if (GetWindowBinding(_activeWorkspaceProfileDocument, displayId, nodeId) is null)
        {
            message = "当前区域还没有窗口绑定。";
            SetStatusMessage(message);
            return false;
        }

        _activeWorkspaceProfileDocument = NormalizeWorkspaceProfile(
            RemoveWindowBinding(_activeWorkspaceProfileDocument, displayId, nodeId));
        RaiseWindowBindingStatusChanged();
        message = "已清除选中区域绑定，正在后台保存工作区。";
        SetStatusMessage(message);
        SaveWorkspaceProfileDocumentInBackground(
            _activeWorkspaceProfileId,
            _activeWorkspaceProfileDocument,
            "选中区域绑定已清除并后台保存");
        return true;
    }

    public bool TryClearAllWorkspaceWindowBindingsFast(out string message)
    {
        message = string.Empty;
        if (!CanEditWorkspaceBindings
            || _activeWorkspaceProfileDocument is null
            || string.IsNullOrWhiteSpace(_activeWorkspaceProfileId))
        {
            message = "请先选中工作区方案并点击“编辑绑定”，再清除全部绑定。";
            SetStatusMessage(message);
            return false;
        }

        var existingBindings = NormalizeWindowBindings(_activeWorkspaceProfileDocument.WindowBindings);
        if (existingBindings.Count == 0)
        {
            message = "当前工作区还没有窗口绑定。";
            SetStatusMessage(message);
            return false;
        }

        _activeWorkspaceProfileDocument = NormalizeWorkspaceProfile(
            _activeWorkspaceProfileDocument with { WindowBindings = new List<WorkspaceWindowBinding>() });
        RaiseWindowBindingStatusChanged();
        message = $"已清除当前工作区 {existingBindings.Count} 个窗口绑定，正在后台保存。";
        SetStatusMessage(message);
        SaveWorkspaceProfileDocumentInBackground(
            _activeWorkspaceProfileId,
            _activeWorkspaceProfileDocument,
            "当前工作区所有窗口绑定已清除并后台保存");
        return true;
    }
}
