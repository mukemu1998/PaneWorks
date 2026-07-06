namespace PaneWorks.App;

internal sealed record WorkspaceRestoreIssue(string Target, string Reason)
{
    public override string ToString()
    {
        return $"- {Target}：{Reason}";
    }
}
