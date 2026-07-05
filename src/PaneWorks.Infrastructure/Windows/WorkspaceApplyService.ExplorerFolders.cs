using System.Runtime.InteropServices;

namespace PaneWorks.Infrastructure.Windows;

public sealed partial class WorkspaceApplyService
{
    private static IReadOnlyDictionary<IntPtr, string> GetExplorerFolderPathsByHandle()
    {
        IReadOnlyDictionary<IntPtr, string> pathsByHandle = new Dictionary<IntPtr, string>();
        var completed = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                pathsByHandle = GetExplorerFolderPathsByHandleCore();
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "PaneWorks Explorer Folder Lookup"
        };

        try
        {
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completed.Wait(ExplorerFolderLookupTimeout)
                ? pathsByHandle
                : new Dictionary<IntPtr, string>();
        }
        catch
        {
            return new Dictionary<IntPtr, string>();
        }
    }

    private static IReadOnlyDictionary<IntPtr, string> GetExplorerFolderPathsByHandleCore()
    {
        var pathsByHandle = new Dictionary<IntPtr, string>();

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return pathsByHandle;
            }

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return pathsByHandle;
            }

            try
            {
                foreach (var window in shell.Windows())
                {
                    try
                    {
                        var handle = new IntPtr(Convert.ToInt64(window.HWND));
                        var locationUrl = Convert.ToString(window.LocationURL) ?? string.Empty;
                        if (TryConvertExplorerLocationUrlToPath(locationUrl, out string folderPath))
                        {
                            pathsByHandle[handle] = folderPath;
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        TryReleaseComObject(window);
                    }
                }
            }
            finally
            {
                TryReleaseComObject(shell);
            }
        }
        catch
        {
        }

        return pathsByHandle;
    }

    private static bool TryConvertExplorerLocationUrlToPath(string locationUrl, out string folderPath)
    {
        folderPath = string.Empty;
        if (string.IsNullOrWhiteSpace(locationUrl))
        {
            return false;
        }

        try
        {
            var uri = new Uri(locationUrl);
            if (!uri.IsFile)
            {
                return false;
            }

            var localPath = Uri.UnescapeDataString(uri.LocalPath);
            if (!Directory.Exists(localPath))
            {
                return false;
            }

            folderPath = TrimTrailingDirectorySeparators(localPath);
            return !string.IsNullOrWhiteSpace(folderPath);
        }
        catch
        {
            return false;
        }
    }

    private static string TrimTrailingDirectorySeparators(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(root)
            && string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrWhiteSpace(trimmed) ? path : trimmed;
    }

    private static void TryReleaseComObject(object? comObject)
    {
        try
        {
            if (comObject is not null && Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
            }
        }
        catch
        {
        }
    }
}
