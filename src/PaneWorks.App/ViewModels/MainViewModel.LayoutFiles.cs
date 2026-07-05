namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    private async void SaveCurrentLayout()
    {
        if (_isSavingLayout || !IsLayoutEditMode)
        {
            return;
        }

        var targetName = CurrentLayoutName;
        var treatAsSaveAs = false;
        if (string.IsNullOrWhiteSpace(_currentLayoutId))
        {
            var initialName = string.Equals(CurrentLayoutName, DefaultBlankWorkspaceName, StringComparison.OrdinalIgnoreCase)
                ? "我的分区布局"
                : CurrentLayoutName;
            var enteredName = PromptForLayoutName("保存新建分区", "请输入分区布局名称。新建分区首次保存会创建一个新的分区文件。", initialName);
            if (enteredName is null)
            {
                return;
            }

            targetName = NormalizeLayoutName(enteredName);
            treatAsSaveAs = true;
        }

        var targetId = Slugify(targetName);
        _isSavingLayout = true;
        SetStatusMessage(treatAsSaveAs ? "正在保存新建分区..." : "正在保存分区布局...");

        try
        {
            await SaveToTargetAsync(targetId, targetName, treatAsSaveAs, notifyOnSuccess: false);
        }
        finally
        {
            _isSavingLayout = false;
        }
    }

    private async void SaveCurrentLayoutAs()
    {
        if (_isSavingLayout || !IsLayoutEditMode)
        {
            return;
        }

        var enteredName = PromptForLayoutName("分区布局另存为", "请输入新分区布局名称。", CurrentLayoutName);
        if (enteredName is null)
        {
            return;
        }

        var targetName = NormalizeLayoutName(enteredName);
        var targetId = Slugify(targetName);
        _isSavingLayout = true;
        SetStatusMessage("正在另存分区布局...");

        try
        {
            await SaveToTargetAsync(targetId, targetName, treatAsSaveAs: true, notifyOnSuccess: false);
        }
        finally
        {
            _isSavingLayout = false;
        }
    }

    private void SetSelectedLayoutAsSnapLayout()
    {
        if (SelectedLayoutItem is null)
        {
            return;
        }

        TrySetSnapLayout(SelectedLayoutItem.Id, notifyOnSuccess: true);
    }
}
