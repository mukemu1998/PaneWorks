using System.Windows;

namespace PaneWorks.App.ViewModels;

public sealed partial class MainViewModel
{
    public string DirtyLabel => IsDirty ? "未保存修改" : "已保存";

    public bool IsDesktopOverlayVisible => IsLayoutEditMode || IsWorkspaceBindingMode;

    public Visibility DesktopOverlayVisibility => IsDesktopOverlayVisible ? Visibility.Visible : Visibility.Collapsed;

    public bool IsLayoutNameEditable => IsLayoutEditMode;

    public bool IsLayoutNameReadOnly => !IsLayoutNameEditable;

    public string LayoutEditModeLabel => IsLayoutEditMode ? "正在编辑分区" : "当前为选择模式";

    public string LayoutEditModeHint => IsLayoutEditMode
        ? "已进入分区编辑：可在桌面覆盖层右键创建分割线、拖动调整比例、右键删除分割。完成后保存或退出编辑。"
        : "主菜单默认只选择吸附布局；需要修改分割线时，先点击“新建分区”“编辑当前选择”或列表里的“打开编辑”，进入编辑后再操作桌面分区。";

    public string WorkspaceBindingModeLabel => IsWorkspaceBindingMode
        ? $"正在编辑“{ActiveWorkspaceProfileName}”：点击桌面区域后绑定窗口；完成后保存或退出绑定。"
        : "先选中一个工作区方案，再点击“编辑绑定”；进入后方案列表会锁定，避免误改其他方案。";

    public bool CanEditWorkspaceBindings => IsWorkspaceBindingMode && IsWorkspaceProfileEnabled;

    public string ActiveSnapLayoutName => _activeSnapLayoutName;

    public string ActiveSnapLayoutId => _activeSnapLayoutId ?? string.Empty;

    public string SnapModifierKey => _appSettings.SnapModifierKey;

    public string RuntimeSessionModifierKey => _appSettings.RuntimeSessionModifierKey;

    public string MinimizeShortcut => _appSettings.MinimizeShortcut;

    public string CurrentDisplayName => SelectedDisplayItem?.Name ?? "主屏幕";

    public string CurrentDisplaySummary => SelectedDisplayItem?.Description ?? string.Empty;

    public string CanvasSubtitle
    {
        get
        {
            var fileLabel = _currentLayoutId is null ? "当前是临时多屏草稿" : $"当前分区文件：{_currentLayoutId}.json";
            return $"编辑屏幕：{CurrentDisplayName}  |  {fileLabel}  |  同一个分区布局文件会同时保存所有屏幕的分割结果。";
        }
    }

    public string StatusLine => string.IsNullOrWhiteSpace(_lastStatusMessage)
        ? CanvasSubtitle
        : $"{CanvasSubtitle}  |  最近操作：{_lastStatusMessage}";

    public string SnapLayoutLabel
    {
        get
        {
            var idLabel = string.IsNullOrWhiteSpace(_activeSnapLayoutId) ? "未绑定到已保存分区布局" : $"{_activeSnapLayoutId}.json";
            return $"当前分区布局：{_activeSnapLayoutName}  |  {idLabel}  |  会在所有屏幕统一生效";
        }
    }

    public string SelectedRegionBindingSummary => BuildSelectedRegionBindingSummary();

    public string SelectedRegionBindingDescription => BuildSelectedRegionBindingDescription();

    public string SelectionLabel
    {
        get
        {
            if (SelectedNodeId is null)
            {
                return "当前没有选中区域或分割线。";
            }

            if (_queryService.IsLeaf(CurrentDocument, SelectedNodeId))
            {
                return $"当前选中区域：{SelectedNodeId}";
            }

            if (_queryService.IsSplit(CurrentDocument, SelectedNodeId))
            {
                return $"当前选中分割线：{SelectedNodeId}";
            }

            return $"当前选中节点：{SelectedNodeId}";
        }
    }

    public string ShortcutSummary
    {
        get
        {
            var startupLabel = _appSettings.LaunchAtStartup ? "已开启" : "未开启";
            var snapLabel = ShortcutGestureHelper.ToDisplayString(_appSettings.SnapModifierKey, "Shift");
            var runtimeLabel = ShortcutGestureHelper.ToDisplayString(_appSettings.RuntimeSessionModifierKey, "Ctrl + Shift");
            var minimizeLabel = ShortcutGestureHelper.ToDisplayString(_appSettings.MinimizeShortcut, "Esc");
            return $"拖动目标窗口时按住：用户分区吸附 {snapLabel}  |  临时调整区吸附 {runtimeLabel}  |  托盘：{minimizeLabel}  |  开机自启：{startupLabel}";
        }
    }
}
