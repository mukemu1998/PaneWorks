using System.IO;

namespace PaneWorks.App.Diagnostics;

public static class PaneWorksDiagnosticState
{
    private static readonly object SyncRoot = new();
    private static readonly string LastWorkspaceRestoreReportPath = Path.Combine(
        PaneWorksLog.LogDirectoryPath,
        "last-workspace-restore.txt");
    private static string _lastWorkspaceRestoreReport = string.Empty;
    private static DateTimeOffset? _lastWorkspaceRestoreReportAt;

    public static string LastWorkspaceRestoreReportFilePath => LastWorkspaceRestoreReportPath;

    public static void SetLastWorkspaceRestoreReport(string report)
    {
        var savedAt = DateTimeOffset.Now;
        var normalizedReport = report.Trim();
        lock (SyncRoot)
        {
            _lastWorkspaceRestoreReport = normalizedReport;
            _lastWorkspaceRestoreReportAt = savedAt;
        }

        WriteLastWorkspaceRestoreReport(normalizedReport, savedAt);
    }

    public static string GetLastWorkspaceRestoreReport()
    {
        lock (SyncRoot)
        {
            if (string.IsNullOrWhiteSpace(_lastWorkspaceRestoreReport) || _lastWorkspaceRestoreReportAt is null)
            {
                return string.Empty;
            }

            return FormatWorkspaceRestoreReport(_lastWorkspaceRestoreReport, _lastWorkspaceRestoreReportAt.Value);
        }
    }

    public static void ClearLastWorkspaceRestoreReport()
    {
        lock (SyncRoot)
        {
            _lastWorkspaceRestoreReport = string.Empty;
            _lastWorkspaceRestoreReportAt = null;
        }

        try
        {
            if (File.Exists(LastWorkspaceRestoreReportPath))
            {
                File.Delete(LastWorkspaceRestoreReportPath);
            }
        }
        catch
        {
            // Diagnostics cleanup is optional.
        }
    }

    private static void WriteLastWorkspaceRestoreReport(string report, DateTimeOffset savedAt)
    {
        try
        {
            Directory.CreateDirectory(PaneWorksLog.LogDirectoryPath);
            File.WriteAllText(
                LastWorkspaceRestoreReportPath,
                FormatWorkspaceRestoreReport(report, savedAt) + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must never interrupt workspace restore.
        }
    }

    private static string FormatWorkspaceRestoreReport(string report, DateTimeOffset savedAt)
    {
        return $"最近工作区恢复结果（{savedAt:yyyy-MM-dd HH:mm:ss zzz}）"
            + Environment.NewLine
            + report;
    }
}
