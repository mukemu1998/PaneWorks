using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PaneWorks.Core.Models;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private async void CreateWorkspaceProfileFromSelectedLayout()
    {
        if (_isSavingWorkspaceProfile)
        {
            return;
        }

        if (SelectedLayoutItem is null || string.IsNullOrWhiteSpace(SelectedLayoutItem.Id))
        {
            ShowErrorMessage("请先在已保存分区列表中选中一个分区，再创建工作区方案。");
            return;
        }

        var layoutId = SelectedLayoutItem.Id;
        var layoutName = SelectedLayoutItem.Name;
        var defaultName = $"{layoutName} 工作区";
        var enteredName = PromptForLayoutName("从选中的保存分区创建工作区", "请输入工作区名称。新工作区会关联当前选中的分区布局。", defaultName);
        if (enteredName is null)
        {
            return;
        }

        var targetName = NormalizeLayoutName(enteredName);
        var targetId = Slugify(targetName);
        var profile = new WorkspaceProfileDocument(
            2,
            targetName,
            layoutId,
            new List<WorkspaceWindowBinding>());

        _isSavingWorkspaceProfile = true;
        SetStatusMessage("正在从选中的保存分区创建工作区...");

        try
        {
            await SaveWorkspaceProfileToTargetAsync(
                targetId,
                profile,
                previousId: null,
                notifyOnSuccess: true);
        }
        finally
        {
            _isSavingWorkspaceProfile = false;
        }
    }

    private async void CreateWorkspaceProfileFromActiveSnapLayout()
    {
        if (_isSavingWorkspaceProfile)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_activeSnapLayoutId))
        {
            ShowErrorMessage("当前吸附选择还不是已保存分区，请先把分区保存后再创建工作区方案。");
            return;
        }

        var layoutId = _activeSnapLayoutId;
        var layoutName = _activeSnapLayoutName;
        var defaultName = $"{layoutName} 工作区";
        var enteredName = PromptForLayoutName("从当前吸附创建工作区", "请输入工作区名称。当前吸附选择会作为这套工作区的分区基础。", defaultName);
        if (enteredName is null)
        {
            return;
        }

        var targetName = NormalizeLayoutName(enteredName);
        var targetId = Slugify(targetName);
        var profile = new WorkspaceProfileDocument(
            2,
            targetName,
            layoutId,
            new List<WorkspaceWindowBinding>());

        _isSavingWorkspaceProfile = true;
        SetStatusMessage("正在从当前吸附创建工作区...");

        try
        {
            await SaveWorkspaceProfileToTargetAsync(
                targetId,
                profile,
                previousId: null,
                notifyOnSuccess: true);
        }
        finally
        {
            _isSavingWorkspaceProfile = false;
        }
    }

    private async void SaveWorkspaceProfile()
    {
        if (_isSavingWorkspaceProfile)
        {
            return;
        }

        if (!IsWorkspaceProfileEnabled
            || _activeWorkspaceProfileDocument is null
            || string.IsNullOrWhiteSpace(_activeWorkspaceProfileId))
        {
            ShowErrorMessage("请先从当前吸附或选中的保存分区创建工作区方案。");
            return;
        }

        _isSavingWorkspaceProfile = true;
        SetStatusMessage("正在保存工作区...");

        try
        {
            await SaveWorkspaceProfileToTargetAsync(
                _activeWorkspaceProfileId,
                _activeWorkspaceProfileDocument,
                previousId: _activeWorkspaceProfileId,
                notifyOnSuccess: false);
        }
        finally
        {
            _isSavingWorkspaceProfile = false;
        }
    }

    private async void SaveWorkspaceProfileAs()
    {
        if (_isSavingWorkspaceProfile)
        {
            return;
        }

        if (!TryBuildWorkspaceProfileDraft(out var profileDraft, out var message))
        {
            ShowErrorMessage(message);
            return;
        }

        var enteredName = PromptForLayoutName("工作区另存为", "请输入新的工作区名称。", profileDraft.Name);
        if (enteredName is null)
        {
            return;
        }

        var targetName = NormalizeLayoutName(enteredName);
        var targetId = Slugify(targetName);
        _isSavingWorkspaceProfile = true;
        SetStatusMessage("正在另存工作区...");

        try
        {
            await SaveWorkspaceProfileToTargetAsync(
                targetId,
                profileDraft with { Name = targetName },
                previousId: null,
                notifyOnSuccess: false);
        }
        finally
        {
            _isSavingWorkspaceProfile = false;
        }
    }

}
