using System.Windows;

namespace PaneWorks.App.Updates;

public partial class UpdateProgressDialog : Window
{
    public UpdateProgressDialog()
    {
        InitializeComponent();
    }

    public void ReportProgress(string status, string detail, double progress)
    {
        StatusTextBlock.Text = status;
        DetailTextBlock.Text = detail;
        var boundedProgress = Math.Clamp(progress, 0, 100);
        DownloadProgressBar.IsIndeterminate = false;
        DownloadProgressBar.Value = boundedProgress;
        ProgressTextBlock.Text = $"{boundedProgress:0}%";
    }

    public void ReportIndeterminate(string status, string detail)
    {
        StatusTextBlock.Text = status;
        DetailTextBlock.Text = detail;
        DownloadProgressBar.IsIndeterminate = true;
        ProgressTextBlock.Text = "请稍候";
    }
}
