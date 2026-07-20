namespace PaneWorks.App.Views;

public static class AboutReleaseHighlights
{
    public static string Get(string versionLabel)
    {
        return ParseVersion(versionLabel) switch
        {
            { Major: 0, Minor: 2, Build: 6 } =>
                "· 新增自动更新：可在设置中开启启动检查，确认后会自动下载、校验并完成覆盖更新。\n" +
                "· 多屏编辑更直观：点击目标屏幕即可切换编辑，当前编辑屏幕会显示醒目的边界与顶部标识。\n" +
                "· 主菜单优化：吸附布局与工作区方案均可折叠，展开时保持菜单位置稳定。",
            { Major: 0, Minor: 2, Build: 5 } =>
                "· 工作区恢复结果更清楚，已吸附、已启动、无法启动和暂未识别的窗口会分别提示。\n" +
                "· 设置页新增诊断入口，可直接打开日志、恢复报告和配置目录，排查问题更方便。\n" +
                "· 二级提示弹窗和工作区健康提示继续打磨，常用操作的反馈更统一、更直观。",
            { Major: 0, Minor: 2, Build: 4 } =>
                "· 工作区恢复更可靠：已绑定的文件夹会按原路径重新打开并吸附。\n" +
                "· 多个资源管理器窗口同时存在时，减少误吸附到其他文件夹的情况。\n" +
                "· 旧版工作区绑定兼容性增强，重新应用工作区时识别更准确。",
            { Major: 0, Minor: 2, Build: 3 } =>
                "· 关于窗口新增当前版本要点与检查更新入口。\n" +
                "· 主菜单、设置窗口与侧边悬浮条交互继续打磨。\n" +
                "· 源码外壳 UI 拆分整理，便于后续版本维护。",
            { Major: 0, Minor: 2, Build: 2 } =>
                "· 临时拓扑吸附：窗口退出后，剩余窗口可重新靠近并恢复联动。\n" +
                "· 平行参考线锁止：拖动到屏幕边缘或平行线后不再越界。\n" +
                "· 细节打磨：优化吸附识别、置顶补偿和画布分割线显示。",
            _ => "· 当前版本持续围绕桌面分区、窗口吸附和工作区恢复体验进行稳定性打磨。"
        };
    }

    public static Version? ParseVersion(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var metadataIndex = normalized.IndexOfAny(['+', '-']);
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        return Version.TryParse(normalized, out var version) ? version : null;
    }
}
