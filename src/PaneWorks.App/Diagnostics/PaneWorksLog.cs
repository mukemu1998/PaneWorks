using System.IO;

namespace PaneWorks.App.Diagnostics;

public static class PaneWorksLog
{
    private const long MaxLogFileBytes = 2 * 1024 * 1024;
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PaneWorks",
        "logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "paneworks.log");
    private static readonly string PreviousLogPath = Path.Combine(LogDirectory, "paneworks.previous.log");

    public static string LogDirectoryPath => LogDirectory;

    public static string LogFilePath => LogPath;

    public static string PreviousLogFilePath => PreviousLogPath;

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Error(string message, Exception exception)
    {
        Write("ERROR", message, exception);
    }

    public static void ClearLogs()
    {
        try
        {
            lock (SyncRoot)
            {
                DeleteIfExists(LogPath);
                DeleteIfExists(PreviousLogPath);
            }
        }
        catch
        {
            // Diagnostics cleanup is optional and must never break settings UI.
        }
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
            if (exception is not null)
            {
                line += $" | {exception.GetType().Name}: {exception.Message}";
            }

            lock (SyncRoot)
            {
                RotateLogIfNeeded();
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never be allowed to break the desktop tool itself.
        }
    }

    private static void RotateLogIfNeeded()
    {
        if (!File.Exists(LogPath))
        {
            return;
        }

        var logFile = new FileInfo(LogPath);
        if (logFile.Length < MaxLogFileBytes)
        {
            return;
        }

        if (File.Exists(PreviousLogPath))
        {
            File.Delete(PreviousLogPath);
        }

        File.Move(LogPath, PreviousLogPath);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
