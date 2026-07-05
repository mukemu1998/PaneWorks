using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    public bool TryUpsertWorkspaceWindowBindings(IReadOnlyList<WorkspaceWindowBinding> bindings, out string message)
    {
        message = string.Empty;
        if (!CanEditWorkspaceBindings || _activeWorkspaceProfileDocument is null)
        {
            message = "请先选中工作区方案并点击“编辑绑定”，再一键绑定已吸附窗口。";
            return false;
        }

        var normalizedBindings = NormalizeWindowBindings(bindings);
        if (normalizedBindings.Count == 0)
        {
            message = "没有找到可保存的已吸附窗口绑定。";
            return false;
        }

        _activeWorkspaceProfileDocument = ReplaceWindowBindingGroups(_activeWorkspaceProfileDocument, normalizedBindings);
        if (!TrySaveActiveWorkspaceProfileDocument(out var saveMessage))
        {
            message = saveMessage;
            return false;
        }

        RaiseWindowBindingStatusChanged();
        message = $"已一键绑定并保存 {normalizedBindings.Count} 个完全吸附的窗口。";
        return true;
    }

    public bool TryUpsertWorkspaceWindowBindingsFast(IReadOnlyList<WorkspaceWindowBinding> bindings, out string message)
    {
        message = string.Empty;
        if (!CanEditWorkspaceBindings
            || _activeWorkspaceProfileDocument is null
            || string.IsNullOrWhiteSpace(_activeWorkspaceProfileId))
        {
            message = "请先选中工作区方案并点击“编辑绑定”，再一键绑定已吸附窗口。";
            SetStatusMessage(message);
            return false;
        }

        var normalizedBindings = NormalizeWindowBindings(bindings);
        if (normalizedBindings.Count == 0)
        {
            message = "没有找到可保存的已吸附窗口绑定。";
            SetStatusMessage(message);
            return false;
        }

        _activeWorkspaceProfileDocument = NormalizeWorkspaceProfile(
            ReplaceWindowBindingGroups(_activeWorkspaceProfileDocument, normalizedBindings));
        RaiseWindowBindingStatusChanged();

        message = $"已一键绑定 {normalizedBindings.Count} 个窗口，正在后台保存工作区。";
        SetStatusMessage(message);
        SaveWorkspaceProfileDocumentInBackground(
            _activeWorkspaceProfileId,
            _activeWorkspaceProfileDocument,
            $"工作区“{_activeWorkspaceProfileName}”已后台保存");
        return true;
    }

    public bool TryUpsertWorkspaceWindowBindingPatch(WorkspaceWindowBinding binding, string statusMessage)
    {
        if (!IsWorkspaceProfileEnabled
            || _activeWorkspaceProfileDocument is null
            || string.IsNullOrWhiteSpace(_activeWorkspaceProfileId))
        {
            return false;
        }

        var normalizedBindings = NormalizeWindowBindings(new[] { binding });
        if (normalizedBindings.Count == 0)
        {
            return false;
        }

        _activeWorkspaceProfileDocument = NormalizeWorkspaceProfile(
            UpsertWindowBindingPatch(_activeWorkspaceProfileDocument, normalizedBindings[0]));
        RaiseWindowBindingStatusChanged();
        SetStatusMessage(statusMessage);
        SaveWorkspaceProfileDocumentInBackground(
            _activeWorkspaceProfileId,
            _activeWorkspaceProfileDocument,
            statusMessage);
        return true;
    }
}
