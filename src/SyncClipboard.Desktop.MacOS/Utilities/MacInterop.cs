using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.MacOS.Utilities;

/// <summary>
/// Native macOS API declarations
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacInterop
{
    private const string ApplicationServicesLib = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    // AXValue types
    public const int kAXValueCGPointType = 1;
    public const int kAXValueCGRectType = 3;
    public const int kAXValueCFRangeType = 4;

    // Error codes
    public const int errAXSuccess = 0;

    #region Structures

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CFRange
    {
        public long Location;
        public long Length;
    }

    #endregion

    #region AXUIElement Functions

    [DllImport(ApplicationServicesLib)]
    public static extern IntPtr AXUIElementCreateSystemWide();

    [DllImport(ApplicationServicesLib)]
    public static extern IntPtr AXUIElementCreateApplication(int pid);

    [DllImport(ApplicationServicesLib)]
    public static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

    [DllImport(ApplicationServicesLib)]
    public static extern int AXUIElementSetAttributeValue(IntPtr element, IntPtr attribute, IntPtr value);

    [DllImport(ApplicationServicesLib)]
    public static extern int AXUIElementCopyParameterizedAttributeValue(IntPtr element, IntPtr attribute, IntPtr parameter, out IntPtr value);

    [DllImport(ApplicationServicesLib)]
    public static extern int AXUIElementCopyActionNames(IntPtr element, out IntPtr names);

    #endregion

    #region AXValue Functions

    [DllImport(ApplicationServicesLib)]
    public static extern int AXValueGetType(IntPtr value);

    [DllImport(ApplicationServicesLib)]
    public static extern IntPtr AXValueCreate(int type, ref CFRange value);

    [DllImport(ApplicationServicesLib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AXValueGetValue(IntPtr value, int type, out CGRect outValue);

    [DllImport(ApplicationServicesLib, EntryPoint = "AXValueGetValue")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AXValueGetValuePoint(IntPtr value, int type, out CGPoint outValue);

    [DllImport(ApplicationServicesLib, EntryPoint = "AXValueGetValue")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AXValueGetValueCFRange(IntPtr value, int type, out CFRange outValue);

    #endregion

    #region Accessibility Permission

    [DllImport(ApplicationServicesLib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AXIsProcessTrusted();

    #endregion

    #region CoreFoundation

    [DllImport(ApplicationServicesLib)]
    public static extern void CFRelease(IntPtr cf);

    #endregion

    #region Helper Methods

    /// <summary>
    /// Create an AXValue from a CFRange
    /// </summary>
    public static IntPtr AXValueCreateFromCFRange(CFRange range)
    {
        return AXValueCreate(kAXValueCFRangeType, ref range);
    }

    /// <summary>
    /// Get human-readable name for AXValue type
    /// </summary>
    public static string GetTypeName(int type)
    {
        return type switch
        {
            0 => "kAXValueIllegalType",
            1 => "kAXValueCGPointType",
            2 => "kAXValueCGSizeType",
            3 => "kAXValueCGRectType",
            4 => "kAXValueCFRangeType",
            5 => "kAXValueAXErrorType",
            _ => $"Unknown({type})"
        };
    }

    /// <summary>
    /// Get human-readable message for AX error code
    /// </summary>
    public static string GetErrorMessage(int errorCode)
    {
        return errorCode switch
        {
            -25200 => "APIDisabled",
            -25201 => "NotImplemented",
            -25202 => "InvalidUIElement",
            -25203 => "CannotSetAttribute",
            -25204 => "AttributeNotFound",
            -25205 => "ActionNotFound",
            -25206 => "NotEnoughPrecision",
            -25207 => "NoValue",
            -25208 => "ParameterizedAttributeNotFound",
            -25209 => "IllegalArgument",
            -25210 => "CannotComplete",
            -25211 => "Failure",
            -25212 => "InvalidUIElementObject",
            -25213 => "Timeout",
            -25214 => "InvalidTextRange",
            -25000 => "NotValid",
            -25001 => "CannotGetPosition",
            -25002 => "CannotSetPosition",
            -25003 => "NoNext",
            -25004 => "NoPrevious",
            -25005 => "CannotWrap",
            -25010 => "NoAncestor",
            -25011 => "NoDescendant",
            -25012 => "Unexpected",
            -25015 => "CannotInteract",
            -25016 => "Duplicate",
            -25017 => "InstanceInvalid",
            -25020 => "CallbackAlreadyRegistered",
            -25021 => "CallbackNotRegistered",
            -25022 => "NoOp",
            -25023 => "APINotFound",
            -25024 => "OutOfMemory",
            -25025 => "NotApplication",
            -25026 => "NotValidForUIElement",
            _ => $"error code: {errorCode}"
        };
    }

    #endregion
}
