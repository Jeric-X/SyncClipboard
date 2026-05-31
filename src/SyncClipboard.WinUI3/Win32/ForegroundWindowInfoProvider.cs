using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Diagnostics;
using System.Text;

namespace SyncClipboard.WinUI3.Win32;

internal sealed class ForegroundWindowInfoProvider(ILogger logger) : IForegroundWindowInfoProvider
{
    private readonly ILogger _logger = logger;
    private const string Tag = "ForegroundWindow";

    public ForegroundWindowInfo GetForegroundWindowInfo()
    {
        try
        {
            var hWnd = User32Interop.GetForegroundWindow();
            if (hWnd == IntPtr.Zero)
            {
                _logger.Write(Tag, "GetForegroundWindow returned null");
                return ForegroundWindowInfo.Invalid;
            }

            var threadId = User32Interop.GetWindowThreadProcessId(hWnd, out var processId);
            if (processId == 0)
            {
                _logger.Write(Tag, $"GetWindowThreadProcessId failed for hwnd={hWnd.ToInt64():X}");
                return ForegroundWindowInfo.Invalid;
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
                    _logger.Write(Tag, $"Failed to get MainModule for process {processId}: {ex.Message}");
                }
            }
            catch (ArgumentException ex)
            {
                _logger.Write(Tag, $"Process {processId} is not running: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Write(Tag, $"Failed to get process {processId}: {ex.Message}");
            }

            var titleBuilder = new StringBuilder(256);
            User32Interop.GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
            var windowTitle = titleBuilder.ToString();

            if (!User32Interop.GetWindowRect(hWnd, out var rect))
            {
                _logger.Write(Tag, $"GetWindowRect failed for hwnd={hWnd.ToInt64():X}");
                return ForegroundWindowInfo.Invalid;
            }

            return new ForegroundWindowInfo
            {
                ProcessName = processName,
                WindowTitle = windowTitle,
                ExecutableName = executableName,
                X = rect.Left,
                Y = rect.Top,
                Width = rect.Right - rect.Left,
                Height = rect.Bottom - rect.Top,
                IsValid = true
            };
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return ForegroundWindowInfo.Invalid;
        }
    }
}
