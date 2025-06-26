using System.Diagnostics;
using System.Runtime.Versioning;

namespace SyncClipboard.Core.Utilities;

public static class Sys
{
    public static Process? OpenWithDefaultApp(string? arg)
    {
        ArgumentNullException.ThrowIfNull(arg);
        return Process.Start(new ProcessStartInfo(arg) { UseShellExecute = true });
    }

    public static void ShowPathInFileManager(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            ShowPathInExplorer(path);
        }
        else if (OperatingSystem.IsLinux())
        {
            XdgOpenFolder(Path.GetDirectoryName(path)!);
        }
        else if (OperatingSystem.IsMacOS())
        {
            ShowPathInFinder(path);
        }
    }

    public static void OpenFolderInFileManager(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            OpenFolderInExplorer(path);
        }
        else if (OperatingSystem.IsLinux())
        {
            XdgOpenFolder(path);
        }
        else if (OperatingSystem.IsMacOS())
        {
            OpenFolderInFinder(path);
        }
    }

    [SupportedOSPlatform("macos")]
    private static void OpenFolderInFinder(string path)
    {
        Process.Start("open", $"\"{path}\"");
    }

    [SupportedOSPlatform("macos")]
    private static void ShowPathInFinder(string path)
    {
        Process.Start("open", $"-R \"{path}\"");
    }

    [SupportedOSPlatform("linux")]
    private static void XdgOpenFolder(string path)
    {
        try
        {
            Process.Start("xdg-open", $"\"{path}\"");
        }
        catch (Exception ex)
        {
            AppCore.TryGetCurrent()?.Logger.Write("Sys", $"Failed to open folder with xdg-open: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ShowPathInExplorer(string path)
    {
        Process.Start("explorer", $"/e,/select,\"{path}\"");
    }

    [SupportedOSPlatform("windows")]
    private static void OpenFolderInExplorer(string path)
    {
        Process.Start("explorer", $"\"{path}\"");
    }
}