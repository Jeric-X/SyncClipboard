using System;
using System.Runtime.Versioning;
using AppKit;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.MacOS.Utilities;

[SupportedOSPlatform("macos")]
internal sealed class ForegroundWindowInfoProvider(ILogger logger) : IForegroundWindowInfoProvider
{
    private readonly ILogger _logger = logger;
    private const string Tag = "ForegroundWindow";

    // Pre-create CFString attributes using NSString (managed by .NET runtime)
    private static readonly IntPtr kAXMainWindowAttribute = MacInteropHelper.CreateCFString("AXMainWindow");
    private static readonly IntPtr kAXTitleAttribute = MacInteropHelper.CreateCFString("AXTitle");
    private static readonly IntPtr kAXPositionAttribute = MacInteropHelper.CreateCFString("AXPosition");
    private static readonly IntPtr kAXSizeAttribute = MacInteropHelper.CreateCFString("AXSize");

    public ForegroundWindowDetail GetForegroundWindowDetail()
    {
        try
        {
            var frontmostApp = NSWorkspace.SharedWorkspace.FrontmostApplication;
            if (frontmostApp == null)
            {
                _logger.Write(Tag, "FrontmostApplication is null");
                return ForegroundWindowDetail.Invalid;
            }

            var pid = frontmostApp.ProcessIdentifier;
            var processName = frontmostApp.LocalizedName ?? string.Empty;
            var executableName = GetExecutableName(pid);

            // Get window title and bounds using Accessibility API
            var (title, bounds) = GetWindowInfo(pid);
            var windowTitle = title ?? string.Empty;

            var windowInfo = new ForegroundWindowInfo
            {
                ProcessName = processName,
                WindowTitle = windowTitle,
                ExecutableName = executableName ?? processName
            };

            if (bounds.HasValue)
            {
                return new ForegroundWindowDetail
                {
                    WindowInfo = windowInfo,
                    X = (int)bounds.Value.X,
                    Y = (int)bounds.Value.Y,
                    Width = (int)bounds.Value.Width,
                    Height = (int)bounds.Value.Height,
                    IsValid = true
                };
            }

            return new ForegroundWindowDetail
            {
                WindowInfo = windowInfo,
                X = 0,
                Y = 0,
                Width = 0,
                Height = 0,
                IsValid = true
            };
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return ForegroundWindowDetail.Invalid;
        }
    }

    public ForegroundWindowInfo? GetForegroundWindowInfo()
    {
        try
        {
            var frontmostApp = NSWorkspace.SharedWorkspace.FrontmostApplication;
            if (frontmostApp == null)
            {
                _logger.Write(Tag, "FrontmostApplication is null");
                return null;
            }

            var pid = frontmostApp.ProcessIdentifier;
            var processName = frontmostApp.LocalizedName ?? string.Empty;
            var executableName = GetExecutableName(pid);

            // Get window title using Accessibility API
            var (title, _) = GetWindowInfo(pid);
            var windowTitle = title ?? string.Empty;

            return new ForegroundWindowInfo
            {
                ProcessName = processName,
                WindowTitle = windowTitle,
                ExecutableName = executableName ?? processName
            };
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get the executable name from the process ID.
    /// </summary>
    private static string? GetExecutableName(int pid)
    {
        try
        {
            var runningApp = NSRunningApplication.GetRunningApplication(pid);
            return runningApp?.BundleIdentifier;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get window title and bounds using Accessibility API.
    /// </summary>
    private (string? Title, MacInterop.CGRect? Bounds) GetWindowInfo(int pid)
    {
        using var appElement = MacInteropHelper.CreateApplication(pid);
        if (appElement.IsInvalid)
        {
            _logger.Write(Tag, $"AXUIElementCreateApplication failed for pid={pid}");
            return (null, null);
        }

        // Get main window
        using var mainWindow = MacInteropHelper.GetMainWindow(appElement.Handle, kAXMainWindowAttribute);
        if (mainWindow == null)
        {
            _logger.Write(Tag, "Failed to get main window");
            return (null, null);
        }

        // Get window title
        var title = MacInteropHelper.GetWindowTitle(mainWindow.Handle, kAXTitleAttribute);

        // Get window position and size
        var bounds = GetWindowBounds(mainWindow.Handle);

        return (title, bounds);
    }

    /// <summary>
    /// Get window bounds (position and size) from a window UI element.
    /// </summary>
    private MacInterop.CGRect? GetWindowBounds(IntPtr windowElement)
    {
        try
        {
            // Get position
            using var positionValue = MacInteropHelper.CopyAttributeValue(windowElement, kAXPositionAttribute);
            if (positionValue == null)
            {
                _logger.Write(Tag, "Failed to get window position");
                return null;
            }

            var positionType = MacInterop.AXValueGetType(positionValue.Handle);
            if (positionType != MacInterop.kAXValueCGPointType ||
                !MacInterop.AXValueGetValuePoint(positionValue.Handle, MacInterop.kAXValueCGPointType, out var position))
            {
                _logger.Write(Tag, $"Window position type mismatch: {positionType}");
                return null;
            }

            // Get size
            using var sizeValue = MacInteropHelper.CopyAttributeValue(windowElement, kAXSizeAttribute);
            if (sizeValue == null)
            {
                _logger.Write(Tag, "Failed to get window size");
                return null;
            }

            var sizeType = MacInterop.AXValueGetType(sizeValue.Handle);
            if (sizeType != MacInterop.kAXValueCGPointType ||
                !MacInterop.AXValueGetValuePoint(sizeValue.Handle, MacInterop.kAXValueCGPointType, out var size))
            {
                _logger.Write(Tag, $"Window size type mismatch: {sizeType}");
                return null;
            }

            return new MacInterop.CGRect
            {
                X = position.X,
                Y = position.Y,
                Width = size.X,
                Height = size.Y
            };
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception getting window bounds: {ex.Message}");
            return null;
        }
    }
}
