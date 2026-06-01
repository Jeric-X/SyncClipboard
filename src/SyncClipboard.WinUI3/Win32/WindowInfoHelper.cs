using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Diagnostics;
using System.Text;

namespace SyncClipboard.WinUI3.Win32;

internal static class WindowInfoHelper
{
    public static ForegroundWindowInfo? GetWindowInfoFromHwnd(IntPtr hWnd, ILogger logger, string tag)
    {
        var threadId = User32Interop.GetWindowThreadProcessId(hWnd, out var processId);
        if (processId == 0)
        {
            logger.Write(tag, $"GetWindowThreadProcessId failed for hwnd={hWnd.ToInt64():X}");
            return null;
        }

        string processName = "";
        string executableName = "";
        try
        {
            var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName ?? "";
            try
            {
                executableName = process.MainModule?.ModuleName ?? "";
            }
            catch (Exception ex)
            {
                logger.Write(tag, $"Failed to get MainModule for process {processId}: {ex.Message}");
            }
        }
        catch (ArgumentException ex)
        {
            logger.Write(tag, $"Process {processId} is not running: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.Write(tag, $"Failed to get process {processId}: {ex.Message}");
        }

        var titleBuilder = new StringBuilder(256);
        _ = User32Interop.GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
        var windowTitle = titleBuilder.ToString();

        if (string.IsNullOrEmpty(processName) && string.IsNullOrEmpty(windowTitle) && string.IsNullOrEmpty(executableName))
        {
            return null;
        }

        return new ForegroundWindowInfo
        {
            ProcessName = processName,
            WindowTitle = windowTitle,
            ExecutableName = executableName
        };
    }
}
