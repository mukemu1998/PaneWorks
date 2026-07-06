namespace PaneWorks.App;

internal sealed record WorkspaceRestoreSummary(
    int BindingCount,
    int MatchedCount,
    int RestoredCount,
    int StartedCount,
    int LaunchSkippedCount,
    int LaunchFailedCount,
    int RegionMissingCount,
    int SnapRejectedCount,
    int StartedButNotRestoredCount,
    IReadOnlyList<WorkspaceRestoreIssue> Issues)
{
    public int ProblemCount =>
        LaunchSkippedCount
        + LaunchFailedCount
        + RegionMissingCount
        + SnapRejectedCount
        + StartedButNotRestoredCount;

    public string ToStatusMessage()
    {
        return ProblemCount == 0
            ? $"工作区恢复完成：已吸附 {RestoredCount} / {BindingCount} 个窗口，启动 {StartedCount} 个。"
            : $"工作区恢复完成：已吸附 {RestoredCount} / {BindingCount} 个窗口，启动 {StartedCount} 个，需检查 {ProblemCount} 项。";
    }

    public string ToDialogMessage()
    {
        var lines = new List<string>
        {
            $"已吸附窗口：{RestoredCount} / {BindingCount}",
            $"已匹配已打开窗口：{MatchedCount}",
            $"已启动缺失窗口：{StartedCount}"
        };

        if (RegionMissingCount > 0)
        {
            lines.Add($"找不到绑定区域：{RegionMissingCount}");
        }

        if (SnapRejectedCount > 0)
        {
            lines.Add($"系统拒绝吸附：{SnapRejectedCount}");
        }

        if (LaunchSkippedCount > 0)
        {
            lines.Add($"无法启动的绑定：{LaunchSkippedCount}");
        }

        if (LaunchFailedCount > 0)
        {
            lines.Add($"启动失败：{LaunchFailedCount}");
        }

        if (StartedButNotRestoredCount > 0)
        {
            lines.Add($"已启动但暂未识别吸附：{StartedButNotRestoredCount}");
        }

        if (ProblemCount > 0)
        {
            lines.Add(string.Empty);
            lines.Add("详情：");
            foreach (var issue in Issues.Take(12))
            {
                lines.Add(issue.ToString());
            }

            if (Issues.Count > 12)
            {
                lines.Add($"- 还有 {Issues.Count - 12} 项未显示，可打开日志目录查看。");
            }

            lines.Add(string.Empty);
            lines.Add("如果某些窗口没有恢复，请确认程序路径、文件夹路径、窗口权限和目标屏幕是否仍然有效。");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
