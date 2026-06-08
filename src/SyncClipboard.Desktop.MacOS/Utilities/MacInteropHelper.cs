using System;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.MacOS.Utilities;

/// <summary>
/// Helper class for MacInterop that returns CFHandle (IDisposable) instead of IntPtr.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacInteropHelper
{
    /// <summary>
    /// Creates a system-wide accessibility UI element.
    /// </summary>
    public static CFHandle CreateSystemWide()
    {
        return new CFHandle(MacInterop.AXUIElementCreateSystemWide());
    }

    /// <summary>
    /// Creates an accessibility UI element for the application with the given PID.
    /// </summary>
    public static CFHandle CreateApplication(int pid)
    {
        return new CFHandle(MacInterop.AXUIElementCreateApplication(pid));
    }

    /// <summary>
    /// Creates an AXValue from a CFRange.
    /// </summary>
    public static CFHandle CreateAXValueFromCFRange(MacInterop.CFRange range)
    {
        return new CFHandle(MacInterop.AXValueCreateFromCFRange(range));
    }

    /// <summary>
    /// Copies the value of an attribute from a UI element.
    /// Returns null if the operation fails.
    /// </summary>
    public static CFHandle? CopyAttributeValue(IntPtr element, IntPtr attribute)
    {
        var result = MacInterop.AXUIElementCopyAttributeValue(element, attribute, out var value);
        if (result != MacInterop.errAXSuccess || value == IntPtr.Zero)
        {
            if (value != IntPtr.Zero)
            {
                MacInterop.CFRelease(value);
            }
            return null;
        }
        return new CFHandle(value);
    }

    /// <summary>
    /// Copies the value of a parameterized attribute from a UI element.
    /// Returns null if the operation fails.
    /// </summary>
    public static CFHandle? CopyParameterizedAttributeValue(IntPtr element, IntPtr attribute, IntPtr parameter)
    {
        var result = MacInterop.AXUIElementCopyParameterizedAttributeValue(element, attribute, parameter, out var value);
        if (result != MacInterop.errAXSuccess || value == IntPtr.Zero)
        {
            if (value != IntPtr.Zero)
            {
                MacInterop.CFRelease(value);
            }
            return null;
        }
        return new CFHandle(value);
    }
}
