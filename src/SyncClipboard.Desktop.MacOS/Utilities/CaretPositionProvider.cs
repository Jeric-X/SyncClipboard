using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.MacOS.Utilities;

[SupportedOSPlatform("macos")]
internal sealed class CaretPositionProvider(ILogger logger) : ICaretPositionProvider
{
    private readonly ILogger _logger = logger;
    private const string Tag = "CaretPosition";

    private const string AppKitLib = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private static readonly IntPtr kAXFocusedUIElementAttribute = GetCFString("AXFocusedUIElement");
    private static readonly IntPtr kAXSelectedTextRangeAttribute = GetCFString("AXSelectedTextRange");
    private static readonly IntPtr kAXBoundsForRangeParameterizedAttribute = GetCFString("AXBoundsForRange");

    [DllImport(CoreFoundationLib, CharSet = CharSet.Unicode)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cString, int encoding);

    [DllImport(CoreFoundationLib)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern IntPtr AXUIElementCreateApplication(int pid);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern int AXUIElementCopyParameterizedAttributeValue(IntPtr element, IntPtr attribute, IntPtr parameter, out IntPtr value);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern int AXValueGetValue(IntPtr value, int type, out CGRect outValue);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AXIsProcessTrusted();

    [DllImport(AppKitLib)]
    private static extern IntPtr NSAccessibilityFocusedUIElement();

    [DllImport(AppKitLib)]
    private static extern IntPtr NSAccessibilitySelectedTextRangeAttribute();

    [DllImport(AppKitLib)]
    private static extern IntPtr NSAccessibilityBoundsForRangeParameterizedAttribute();

    [DllImport("libc")]
    private static extern int getpid();

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    private const int kAXValueCGPointType = 1;
    private const int kAXValueCGSizeType = 2;
    private const int kAXValueCGRectType = 3;
    private const int kAXValueCFRangeType = 4;

    private const int errAXSuccess = 0;

    private static IntPtr GetCFString(string str)
    {
        return CFStringCreateWithCString(IntPtr.Zero, str, 0x08000100);
    }

    public ScreenPosition GetCaretPosition()
    {
        if (!AXIsProcessTrusted())
        {
            _logger.Write(Tag, "Accessibility permission not granted");
            return ScreenPosition.Invalid;
        }

        try
        {
            var focusedElement = GetFocusedUIElement();
            if (focusedElement == IntPtr.Zero)
            {
                _logger.Write(Tag, "Failed to get focused UI element");
                return ScreenPosition.Invalid;
            }

            try
            {
                var bounds = GetCaretBounds(focusedElement);
                if (bounds.HasValue)
                {
                    _logger.Write(Tag, $"Caret position: ({(int)bounds.Value.X}, {(int)bounds.Value.Y})");
                    return new ScreenPosition
                    {
                        X = (int)bounds.Value.X,
                        Y = (int)bounds.Value.Y,
                        IsValid = true
                    };
                }
                _logger.Write(Tag, "Failed to get caret bounds");
            }
            finally
            {
                CFRelease(focusedElement);
            }
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
        }

        return ScreenPosition.Invalid;
    }

    private IntPtr GetFocusedUIElement()
    {
        var pid = getpid();
        var appElement = AXUIElementCreateApplication(pid);
        if (appElement == IntPtr.Zero)
        {
            _logger.Write(Tag, "AXUIElementCreateApplication returned null");
            return IntPtr.Zero;
        }

        try
        {
            var result = AXUIElementCopyAttributeValue(appElement, kAXFocusedUIElementAttribute, out var focusedElement);
            CFRelease(appElement);

            if (result == errAXSuccess && focusedElement != IntPtr.Zero)
            {
                return focusedElement;
            }
            _logger.Write(Tag, $"AXUIElementCopyAttributeValue failed with error: {result}");
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception in GetFocusedUIElement: {ex.Message}");
            CFRelease(appElement);
        }

        return IntPtr.Zero;
    }

    private CGRect? GetCaretBounds(IntPtr element)
    {
        var result = AXUIElementCopyAttributeValue(element, kAXSelectedTextRangeAttribute, out var textRangeValue);
        if (result != errAXSuccess || textRangeValue == IntPtr.Zero)
        {
            _logger.Write(Tag, $"Failed to get selected text range, error: {result}");
            return null;
        }

        try
        {
            result = AXUIElementCopyParameterizedAttributeValue(element, kAXBoundsForRangeParameterizedAttribute, textRangeValue, out var boundsValue);
            CFRelease(textRangeValue);

            if (result != errAXSuccess || boundsValue == IntPtr.Zero)
            {
                _logger.Write(Tag, $"Failed to get bounds for range, error: {result}");
                return null;
            }

            try
            {
                if (AXValueGetValue(boundsValue, kAXValueCGRectType, out var bounds))
                {
                    return bounds;
                }
                _logger.Write(Tag, "AXValueGetValue returned false");
            }
            finally
            {
                CFRelease(boundsValue);
            }
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception in GetCaretBounds: {ex.Message}");
            CFRelease(textRangeValue);
        }

        return null;
    }
}
