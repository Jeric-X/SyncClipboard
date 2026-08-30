using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using AppKit;
using Foundation;
using ObjCRuntime;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.MacOS.Utilities;

[SupportedOSPlatform("macos")]
internal sealed class MacNativeWindowController(ILogger logger, IThreadDispatcher threadDispatcher) : INativeWindowController
{
    private readonly ILogger _logger = logger;
    private readonly IThreadDispatcher _threadDispatcher = threadDispatcher;
    private const string Tag = "ForegroundWindow";

    // Pre-create CFString attributes using NSString (managed by .NET runtime)
    private static readonly IntPtr kAXMainWindowAttribute = MacInteropHelper.CreateCFString("AXMainWindow");
    private static readonly IntPtr kAXFocusedWindowAttribute = MacInteropHelper.CreateCFString("AXFocusedWindow");
    private static readonly IntPtr kAXTitleAttribute = MacInteropHelper.CreateCFString("AXTitle");
    private static readonly IntPtr kAXPositionAttribute = MacInteropHelper.CreateCFString("AXPosition");
    private static readonly IntPtr kAXSizeAttribute = MacInteropHelper.CreateCFString("AXSize");
    private static readonly IntPtr kAXWindowsAttribute = MacInteropHelper.CreateCFString("AXWindows");
    private static readonly IntPtr kAXRaiseAction = MacInteropHelper.CreateCFString("AXRaise");
    private static readonly IntPtr kAXFrontmostAttribute = MacInteropHelper.CreateCFString("AXFrontmost");
    private static readonly IntPtr kAXMainAttribute = MacInteropHelper.CreateCFString("AXMain");
    private static readonly IntPtr kAXFocusedAttribute = MacInteropHelper.CreateCFString("AXFocused");
    private static readonly IntPtr kAXWindowNumberAttribute = MacInteropHelper.CreateCFString("AXWindowNumber");

    public WindowDetail? GetForegroundWindowDetail()
    {
        try
        {
            var frontmostApp = GetFrontmostApplication();
            if (frontmostApp == null)
            {
                _logger.Write(Tag, "FrontmostApplication is null");
                return null;
            }

            var pid = frontmostApp.ProcessIdentifier;
            var processName = frontmostApp.LocalizedName ?? string.Empty;
            var executableName = GetExecutableName(pid);

            // Get window title and bounds using Accessibility API
            var (title, bounds, windowNumber) = GetWindowInfo(pid);
            var windowTitle = title ?? string.Empty;

            var windowInfo = new WindowInfo
            {
                ProcessName = processName,
                WindowTitle = windowTitle,
                ExecutableName = executableName ?? processName
            };

            var screenBounds = ToScreenPosition(bounds);
            var result = new WindowDetail
            {
                WindowInfo = windowInfo,
                Bounds = screenBounds,
                NativeWindowInfo = new MacNativeWindowInfo
                {
                    ProcessId = pid,
                    BundleIdentifier = frontmostApp.BundleIdentifier ?? string.Empty,
                    WindowNumber = windowNumber,
                    WindowTitle = windowTitle,
                    Bounds = screenBounds
                }
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return null;
        }
    }

    public WindowInfo? GetForegroundWindowInfo()
    {
        return GetForegroundWindowDetail()?.WindowInfo;
    }

    public WindowDetail? GetWindowDetail(NativeWindowInfo window)
    {
        if (window is not MacNativeWindowInfo macWindow)
        {
            return null;
        }

        try
        {
            var application = NSRunningApplication.GetRunningApplication(macWindow.ProcessId);
            if (application is null || application.Terminated)
            {
                return null;
            }

            var (title, bounds, windowNumber) = GetWindowInfo(macWindow.ProcessId);
            var screenBounds = ToScreenPosition(bounds) ?? macWindow.Bounds;
            var currentWindow = macWindow with
            {
                WindowTitle = title ?? macWindow.WindowTitle,
                Bounds = screenBounds,
                WindowNumber = windowNumber ?? macWindow.WindowNumber
            };
            return new WindowDetail
            {
                WindowInfo = new WindowInfo
                {
                    ProcessName = application.LocalizedName ?? string.Empty,
                    WindowTitle = currentWindow.WindowTitle,
                    ExecutableName = application.BundleIdentifier ?? currentWindow.BundleIdentifier
                },
                Bounds = screenBounds,
                NativeWindowInfo = currentWindow
            };
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return null;
        }
    }

    public bool TryActivateWindow(NativeWindowInfo window)
    {
        if (window is not MacNativeWindowInfo macWindow)
        {
            _logger.Write(Tag, $"Unsupported native window type: {window.GetType().Name}");
            return false;
        }

        try
        {
            _logger.Write(Tag, $"macOS activation requested: {DescribeWindow(macWindow)}.");
            return _threadDispatcher.RunOnMainThreadAsync(
                () => Task.FromResult(TryActivateWindowOnMainThread(macWindow))).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Failed to activate macOS window: {ex.Message}");
            return false;
        }
    }

    private bool TryActivateWindowOnMainThread(MacNativeWindowInfo macWindow)
    {
        var application = NSRunningApplication.GetRunningApplication(macWindow.ProcessId);
        if (application is null || application.Terminated)
        {
            _logger.Write(Tag, $"Target process {macWindow.ProcessId} is no longer running.");
            return false;
        }

        if (!string.IsNullOrEmpty(macWindow.BundleIdentifier)
            && !string.Equals(
                application.BundleIdentifier,
                macWindow.BundleIdentifier,
                StringComparison.Ordinal))
        {
            _logger.Write(
                Tag,
                $"Target process {macWindow.ProcessId} bundle identifier changed from "
                + $"'{macWindow.BundleIdentifier}' to '{application.BundleIdentifier ?? string.Empty}'.");
            return false;
        }

        var activated = application.Activate(NSApplicationActivationOptions.ActivateIgnoringOtherWindows);
        if (!activated)
        {
            _logger.Write(Tag, $"Failed to activate process {macWindow.ProcessId} ({macWindow.BundleIdentifier}).");
            return false;
        }

        if (!HasWindowIdentity(macWindow))
        {
            _logger.Write(Tag, $"Activated application without a precise window identity: {DescribeWindow(macWindow)}.");
            return true;
        }

        var raised = TryRaiseCapturedWindow(macWindow);
        if (!raised)
        {
            _logger.Write(Tag, $"Failed to raise the captured window in process {macWindow.ProcessId}.");
        }
        else
        {
            _logger.Write(Tag, $"Raised captured macOS window: {DescribeWindow(macWindow)}.");
        }
        return raised;
    }

    private NSRunningApplication? GetFrontmostApplication()
    {
        return _threadDispatcher.RunOnMainThreadAsync(() => Task.FromResult(NSWorkspace.SharedWorkspace.FrontmostApplication)).GetAwaiter().GetResult();
    }

    private static ScreenPosition? ToScreenPosition(MacInterop.CGRect? bounds) => bounds.HasValue
        ? new ScreenPosition
        {
            X = (int)bounds.Value.X,
            Y = (int)bounds.Value.Y,
            Width = (int)bounds.Value.Width,
            Height = (int)bounds.Value.Height
        }
        : null;

    private static bool HasWindowIdentity(MacNativeWindowInfo window) =>
        window.WindowNumber.HasValue || !string.IsNullOrEmpty(window.WindowTitle);

    private static string DescribeWindow(MacNativeWindowInfo window)
    {
        var bounds = window.Bounds is null
            ? "null"
            : $"{window.Bounds.X},{window.Bounds.Y},{window.Bounds.Width}x{window.Bounds.Height}";
        return $"pid={window.ProcessId}, window={window.WindowNumber?.ToString() ?? "null"}, title='{window.WindowTitle}', bounds={bounds}";
    }

    private bool TryRaiseCapturedWindow(MacNativeWindowInfo capturedWindow)
    {
        using var appElement = MacInteropHelper.CreateApplication(capturedWindow.ProcessId);
        if (appElement.IsInvalid)
        {
            return false;
        }

        using var windows = MacInteropHelper.CopyAttributeValue(appElement.Handle, kAXWindowsAttribute);
        if (windows is null)
        {
            return false;
        }

        var count = MacInterop.CFArrayGetCount(windows.Handle);
        for (nint index = 0; index < count; index++)
        {
            var windowElement = MacInterop.CFArrayGetValueAtIndex(windows.Handle, index);
            if (windowElement == IntPtr.Zero)
            {
                continue;
            }

            var candidate = new MacNativeWindowInfo
            {
                ProcessId = capturedWindow.ProcessId,
                BundleIdentifier = capturedWindow.BundleIdentifier,
                WindowNumber = GetWindowNumber(windowElement),
                WindowTitle = MacInteropHelper.GetWindowTitle(windowElement, kAXTitleAttribute) ?? string.Empty,
                Bounds = ToScreenPosition(GetWindowBounds(windowElement))
            };
            if (!capturedWindow.IsSameWindow(candidate))
            {
                continue;
            }

            using var trueValue = NSNumber.FromBoolean(true);
            _ = MacInterop.AXUIElementSetAttributeValue(appElement.Handle, kAXFrontmostAttribute, trueValue.Handle);
            _ = MacInterop.AXUIElementSetAttributeValue(windowElement, kAXMainAttribute, trueValue.Handle);
            _ = MacInterop.AXUIElementSetAttributeValue(windowElement, kAXFocusedAttribute, trueValue.Handle);
            return MacInterop.AXUIElementPerformAction(windowElement, kAXRaiseAction) == MacInterop.errAXSuccess;
        }

        return false;
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
    private (string? Title, MacInterop.CGRect? Bounds, long? WindowNumber) GetWindowInfo(int pid)
    {
        if (!MacInterop.AXIsProcessTrusted())
        {
            return (null, null, null);
        }

        using var appElement = MacInteropHelper.CreateApplication(pid);
        if (appElement.IsInvalid)
        {
            _logger.Write(Tag, $"AXUIElementCreateApplication failed for pid={pid}");
            return (null, null, null);
        }

        // Some applications (including Codex/Electron builds) don't expose
        // AXMainWindow, but do expose AXFocusedWindow while they are frontmost.
        using var activeWindow = MacInteropHelper.CopyAttributeValue(appElement.Handle, kAXFocusedWindowAttribute)
            ?? MacInteropHelper.GetMainWindow(appElement.Handle, kAXMainWindowAttribute);
        if (activeWindow == null)
        {
            return (null, null, null);
        }

        // Get window title
        var title = MacInteropHelper.GetWindowTitle(activeWindow.Handle, kAXTitleAttribute);

        // Get window position and size
        var bounds = GetWindowBounds(activeWindow.Handle);

        return (title, bounds, GetWindowNumber(activeWindow.Handle));
    }

    private static long? GetWindowNumber(IntPtr windowElement)
    {
        using var numberValue = MacInteropHelper.CopyAttributeValue(windowElement, kAXWindowNumberAttribute);
        return numberValue is null
            ? null
            : Runtime.GetNSObject<NSNumber>(numberValue.Handle)?.Int64Value;
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
            if (sizeType != MacInterop.kAXValueCGSizeType ||
                !MacInterop.AXValueGetValueSize(sizeValue.Handle, MacInterop.kAXValueCGSizeType, out var size))
            {
                _logger.Write(Tag, $"Window size type mismatch: {sizeType}, expected: {MacInterop.kAXValueCGSizeType}");
                return null;
            }

            return new MacInterop.CGRect
            {
                X = position.X,
                Y = position.Y,
                Width = size.Width,
                Height = size.Height
            };
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception getting window bounds: {ex.Message}");
            return null;
        }
    }
}
