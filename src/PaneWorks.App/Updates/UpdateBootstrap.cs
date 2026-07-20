using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace PaneWorks.App.Updates;

internal static class UpdateBootstrap
{
    private const string ApplyUpdateArgument = "--apply-update";

    public static bool TryApplyPendingUpdate(string[] arguments)
    {
        if (arguments.Length == 0 || !string.Equals(arguments[0], ApplyUpdateArgument, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var options = ParseOptions(arguments.Skip(1));
            ApplyUpdate(
                GetRequiredOption(options, "--parent-pid"),
                GetRequiredOption(options, "--package"),
                GetRequiredOption(options, "--target"),
                GetRequiredOption(options, "--restart"));
            return true;
        }
        catch
        {
            return true;
        }
    }

    public static void LaunchUpdater(string packagePath)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            throw new InvalidOperationException("无法定位当前 PaneWorks 程序文件。");
        }

        var installDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var updaterDirectory = Path.Combine(Path.GetTempPath(), "PaneWorks", "updater", Guid.NewGuid().ToString("N"));
        CopyDirectory(installDirectory, updaterDirectory);

        var updaterPath = Path.Combine(updaterDirectory, Path.GetFileName(processPath));
        if (!File.Exists(updaterPath))
        {
            throw new FileNotFoundException("未能准备更新器程序。", updaterPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            WorkingDirectory = updaterDirectory,
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(ApplyUpdateArgument);
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("--package");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(installDirectory);
        startInfo.ArgumentList.Add("--restart");
        startInfo.ArgumentList.Add(Path.GetFileName(processPath));

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("无法启动 PaneWorks 更新器。");
        }
    }

    private static void ApplyUpdate(string parentProcessId, string packagePath, string targetDirectory, string restartFileName)
    {
        if (!int.TryParse(parentProcessId, out var processId)
            || !File.Exists(packagePath)
            || string.IsNullOrWhiteSpace(restartFileName))
        {
            throw new InvalidDataException("更新参数无效。");
        }

        WaitForParentExit(processId);

        var extractionDirectory = Path.Combine(Path.GetDirectoryName(packagePath)!, "extracted");
        Directory.CreateDirectory(extractionDirectory);
        ZipFile.ExtractToDirectory(packagePath, extractionDirectory, overwriteFiles: true);
        var payload = FindPayload(extractionDirectory, restartFileName);
        ReplaceInstallDirectory(Path.GetFullPath(targetDirectory), payload.Directory);

        var restartPath = Path.Combine(targetDirectory, payload.ExecutableFileName);
        if (!File.Exists(restartPath))
        {
            throw new FileNotFoundException("更新后未找到 PaneWorks 主程序。", restartPath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = restartPath,
            WorkingDirectory = targetDirectory,
            UseShellExecute = true
        });
    }

    private static void WaitForParentExit(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited && !process.WaitForExit(45000))
            {
                throw new TimeoutException("等待 PaneWorks 退出超时。");
            }
        }
        catch (ArgumentException)
        {
            // The main process already exited before the updater started.
        }

        Thread.Sleep(700);
    }

    private static UpdatePayload FindPayload(string extractionDirectory, string preferredExecutableName)
    {
        var candidateNames = new[]
        {
            preferredExecutableName,
            "PaneWorks.exe",
            "PaneWorks.App.exe"
        };

        foreach (var candidateName in candidateNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var executablePath = File.Exists(Path.Combine(extractionDirectory, candidateName))
                ? Path.Combine(extractionDirectory, candidateName)
                : Directory.EnumerateFiles(extractionDirectory, candidateName, SearchOption.AllDirectories)
                    .FirstOrDefault();
            if (executablePath is not null)
            {
                return new UpdatePayload(
                    Path.GetDirectoryName(executablePath)!,
                    Path.GetFileName(executablePath));
            }
        }

        throw new InvalidDataException("更新包中未找到 PaneWorks 主程序。");
    }

    private static void ReplaceInstallDirectory(string targetDirectory, string payloadDirectory)
    {
        var backupDirectory = targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + ".update-backup-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(backupDirectory);

        try
        {
            MoveDirectoryContents(targetDirectory, backupDirectory);
            CopyDirectory(payloadDirectory, targetDirectory);
            Directory.Delete(backupDirectory, recursive: true);
        }
        catch
        {
            TryRestoreBackup(targetDirectory, backupDirectory);
            throw;
        }
    }

    private static void MoveDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(sourceDirectory))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
            if (Directory.Exists(sourcePath))
            {
                Directory.Move(sourcePath, destinationPath);
            }
            else
            {
                File.Move(sourcePath, destinationPath);
            }
        }
    }

    private static void TryRestoreBackup(string targetDirectory, string backupDirectory)
    {
        try
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }

            Directory.CreateDirectory(targetDirectory);
            if (Directory.Exists(backupDirectory))
            {
                MoveDirectoryContents(backupDirectory, targetDirectory);
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
        catch
        {
            // The original exception remains the most useful failure signal.
        }
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> arguments)
    {
        var values = arguments.ToArray();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index + 1 < values.Length; index += 2)
        {
            options[values[index]] = values[index + 1];
        }

        return options;
    }

    private static string GetRequiredOption(IReadOnlyDictionary<string, string> options, string name)
    {
        return options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"缺少更新参数：{name}");
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationFile = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private sealed record UpdatePayload(string Directory, string ExecutableFileName);
}
