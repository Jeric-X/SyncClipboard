using System;
using System.Runtime.Versioning;
using AppKit;
using Foundation;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.MacOS.Utilities;

[SupportedOSPlatform("macos")]
internal sealed class CaretPositionProvider(ILogger logger) : ICaretPositionProvider
{
    private readonly ILogger _logger = logger;
    private const string Tag = "CaretPosition";

    // Pre-create CFString attributes using NSString
    private static readonly IntPtr kAXFocusedUIElementAttribute = CreateCFString("AXFocusedUIElement");
    private static readonly IntPtr kAXSelectedTextRangeAttribute = CreateCFString("AXSelectedTextRange");
    private static readonly IntPtr kAXBoundsForRangeParameterizedAttribute = CreateCFString("AXBoundsForRange");
    private static readonly IntPtr kAXManualAccessibilityAttribute = CreateCFString("AXManualAccessibility");

    /// <summary>
    /// Create a CFString using NSString helper (managed by .NET runtime)
    /// </summary>
    private static IntPtr CreateCFString(string str)
    {
        // NSString.CreateNative returns a toll-free bridged CFString
        // The returned IntPtr is managed by the NSString object, so we don't need to CFRelease it
        return NSString.CreateNative(str);
    }

    public ScreenPosition? GetCaretPosition()
    {
        if (!AccessibilityNativeMethods.AXIsProcessTrusted())
        {
            _logger.Write(Tag, "Accessibility permission not granted");
            return null;
        }

        try
        {
            var focusedElement = GetFocusedUIElement();
            if (focusedElement == IntPtr.Zero)
            {
                _logger.Write(Tag, "Failed to get focused UI element");
                return null;
            }

            try
            {
                var bounds = GetCaretBounds(focusedElement);
                if (bounds.HasValue)
                {
                    _logger.Write(Tag, $"Caret position: ({(int)bounds.Value.X}, {(int)bounds.Value.Y}), size: {(int)bounds.Value.Width}x{(int)bounds.Value.Height}");
                    return new ScreenPosition
                    {
                        X = (int)bounds.Value.X,
                        Y = (int)bounds.Value.Y,
                        Width = (int)bounds.Value.Width,
                        Height = (int)bounds.Value.Height
                    };
                }
                _logger.Write(Tag, "Failed to get caret bounds");
            }
            finally
            {
                AccessibilityNativeMethods.CFRelease(focusedElement);
            }
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
        }

        return null;
    }

    private IntPtr GetFocusedUIElement()
    {
        // Enable accessibility for Electron apps (VSCode, Chrome, etc.)
        EnableAccessibilityForFrontmostApp();

        var systemWide = AccessibilityNativeMethods.AXUIElementCreateSystemWide();
        if (systemWide == IntPtr.Zero)
        {
            _logger.Write(Tag, "AXUIElementCreateSystemWide returned null");
            return IntPtr.Zero;
        }

        try
        {
            var result = AccessibilityNativeMethods.AXUIElementCopyAttributeValue(systemWide, kAXFocusedUIElementAttribute, out var focusedElement);
            AccessibilityNativeMethods.CFRelease(systemWide);

            if (result != AccessibilityNativeMethods.errAXSuccess)
            {
                var errorMsg = AccessibilityNativeMethods.GetErrorMessage(result);
                _logger.Write(Tag, $"AXUIElementCopyAttributeValue failed: {errorMsg}");
                return IntPtr.Zero;
            }

            if (focusedElement == IntPtr.Zero)
            {
                _logger.Write(Tag, "AXUIElementCopyAttributeValue returned null focusedElement");
                return IntPtr.Zero;
            }

            return focusedElement;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception in GetFocusedUIElement: {ex.Message}");
            AccessibilityNativeMethods.CFRelease(systemWide);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Enable accessibility for Electron apps by setting AXManualAccessibility attribute.
    /// This is required for apps like VSCode, Chrome, Slack, Discord, etc.
    /// </summary>
    private void EnableAccessibilityForFrontmostApp()
    {
        try
        {
            var frontmostApp = NSWorkspace.SharedWorkspace.FrontmostApplication;
            if (frontmostApp == null)
            {
                return;
            }

            var pid = frontmostApp.ProcessIdentifier;
            var appElement = AccessibilityNativeMethods.AXUIElementCreateApplication(pid);
            if (appElement == IntPtr.Zero)
            {
                return;
            }

            try
            {
                // Set AXManualAccessibility to true for Electron apps
                // NSNumber and CFBoolean are toll-free bridged
                using var trueValue = NSNumber.FromBoolean(true);
                var result = AccessibilityNativeMethods.AXUIElementSetAttributeValue(appElement, kAXManualAccessibilityAttribute, trueValue.Handle);

                if (result == AccessibilityNativeMethods.errAXSuccess)
                {
                    _logger.Write(Tag, $"Enabled accessibility for: {frontmostApp.LocalizedName}");
                }
            }
            finally
            {
                AccessibilityNativeMethods.CFRelease(appElement);
            }
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception enabling accessibility: {ex.Message}");
        }
    }

    private AccessibilityNativeMethods.CGRect? GetCaretBounds(IntPtr element)
    {
        // First, try to get selected text range
        var result = AccessibilityNativeMethods.AXUIElementCopyAttributeValue(element, kAXSelectedTextRangeAttribute, out var textRangeValue);
        if (result != AccessibilityNativeMethods.errAXSuccess || textRangeValue == IntPtr.Zero)
        {
            _logger.Write(Tag, $"Failed to get selected text range, error: {AccessibilityNativeMethods.GetErrorMessage(result)}");
            return null;
        }

        try
        {
            // Log the text range type
            var rangeType = AccessibilityNativeMethods.AXValueGetType(textRangeValue);
            _logger.Write(Tag, $"TextRange type: {rangeType} ({AccessibilityNativeMethods.GetTypeName(rangeType)})");

            // Try to get bounds for the range (works for both selection and insertion point in most native apps)
            result = AccessibilityNativeMethods.AXUIElementCopyParameterizedAttributeValue(element, kAXBoundsForRangeParameterizedAttribute, textRangeValue, out var boundsValue);

            if (result == AccessibilityNativeMethods.errAXSuccess && boundsValue != IntPtr.Zero)
            {
                try
                {
                    // Check the actual type of the AXValue
                    var actualType = AccessibilityNativeMethods.AXValueGetType(boundsValue);
                    _logger.Write(Tag, $"AXValue actual type: {actualType} ({AccessibilityNativeMethods.GetTypeName(actualType)})");

                    AccessibilityNativeMethods.CGRect? bounds = null;

                    if (actualType == AccessibilityNativeMethods.kAXValueCGRectType)
                    {
                        if (AccessibilityNativeMethods.AXValueGetValue(boundsValue, AccessibilityNativeMethods.kAXValueCGRectType, out var rect))
                        {
                            _logger.Write(Tag, $"Raw bounds: X={rect.X}, Y={rect.Y}, W={rect.Width}, H={rect.Height}");
                            bounds = rect;
                        }
                        else
                        {
                            _logger.Write(Tag, "AXValueGetValue returned false for CGRect");
                        }
                    }
                    else if (actualType == AccessibilityNativeMethods.kAXValueCGPointType)
                    {
                        // Some apps return CGPoint instead of CGRect for caret position
                        if (AccessibilityNativeMethods.AXValueGetValuePoint(boundsValue, AccessibilityNativeMethods.kAXValueCGPointType, out var point))
                        {
                            bounds = new AccessibilityNativeMethods.CGRect { X = point.X, Y = point.Y, Width = 1, Height = 1 };
                        }
                        else
                        {
                            _logger.Write(Tag, "AXValueGetValue returned false for CGPoint");
                        }
                    }
                    else
                    {
                        _logger.Write(Tag, $"Unsupported AXValue type: {actualType} ({AccessibilityNativeMethods.GetTypeName(actualType)})");
                    }

                    // Check if we should try insertion point method instead
                    if (bounds.HasValue)
                    {
                        // Check if length == 0 and point is not on screen
                        bool shouldTryInsertionPoint = false;

                        if (rangeType == AccessibilityNativeMethods.kAXValueCFRangeType &&
                            AccessibilityNativeMethods.AXValueGetValueCFRange(textRangeValue, AccessibilityNativeMethods.kAXValueCFRangeType, out var cfRange))
                        {
                            if (cfRange.Length == 0 && !IsPointOnScreen(bounds.Value.X, bounds.Value.Y))
                            {
                                _logger.Write(Tag, $"Length is 0 and point ({bounds.Value.X}, {bounds.Value.Y}) is not on screen, trying insertion point method");
                                shouldTryInsertionPoint = true;
                            }
                        }

                        if (!shouldTryInsertionPoint)
                        {
                            AccessibilityNativeMethods.CFRelease(textRangeValue);
                            return bounds;
                        }
                    }
                }
                finally
                {
                    AccessibilityNativeMethods.CFRelease(boundsValue);
                }
            }

            // If the above failed, try the insertion point method
            _logger.Write(Tag, $"Failed to get bounds for range, error: {AccessibilityNativeMethods.GetErrorMessage(result)}, trying insertion point method");
            return GetCaretBoundsFromInsertionPoint(element, textRangeValue);
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception in GetCaretBounds: {ex.Message}");
            AccessibilityNativeMethods.CFRelease(textRangeValue);
        }

        return null;
    }

    /// <summary>
    /// Check if a point is within any screen bounds
    /// </summary>
    private bool IsPointOnScreen(double x, double y)
    {
        try
        {
            var screens = NSScreen.Screens;
            if (screens == null || screens.Length == 0)
            {
                return true; // Can't determine, assume on screen
            }

            foreach (var screen in screens)
            {
                var frame = screen.Frame;
                // macOS coordinate system: origin is at bottom-left
                if (x >= frame.X && x <= frame.X + frame.Width &&
                    y >= frame.Y && y <= frame.Y + frame.Height)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception checking screen bounds: {ex.Message}");
            return true; // On error, assume on screen to avoid unnecessary fallback
        }
    }

    /// <summary>
    /// Try to get caret bounds when AXBoundsForRange failed
    /// </summary>
    private AccessibilityNativeMethods.CGRect? GetCaretBoundsFromInsertionPoint(IntPtr element, IntPtr textRangeValue)
    {
        try
        {
            var rangeType = AccessibilityNativeMethods.AXValueGetType(textRangeValue);
            if (rangeType != AccessibilityNativeMethods.kAXValueCFRangeType)
            {
                _logger.Write(Tag, $"TextRange is not CFRange: {rangeType}");
                return null;
            }

            if (!AccessibilityNativeMethods.AXValueGetValueCFRange(textRangeValue, AccessibilityNativeMethods.kAXValueCFRangeType, out var cfRange))
            {
                _logger.Write(Tag, "Failed to get CFRange value");
                return null;
            }

            _logger.Write(Tag, $"Insertion point at location: {cfRange.Location}, length: {cfRange.Length}");

            // Try to get bounds for a single character at the insertion point
            // by creating a CFRange with length 1
            var singleCharRange = new AccessibilityNativeMethods.CFRange { Location = cfRange.Location, Length = 1 };
            var axRangeValue = AccessibilityNativeMethods.AXValueCreateFromCFRange(singleCharRange);
            if (axRangeValue == IntPtr.Zero)
            {
                _logger.Write(Tag, "Failed to create AXValue for single char range");
                return null;
            }

            try
            {
                var result = AccessibilityNativeMethods.AXUIElementCopyParameterizedAttributeValue(element, kAXBoundsForRangeParameterizedAttribute, axRangeValue, out var boundsValue);
                AccessibilityNativeMethods.CFRelease(axRangeValue);

                if (result == AccessibilityNativeMethods.errAXSuccess && boundsValue != IntPtr.Zero)
                {
                    try
                    {
                        if (AccessibilityNativeMethods.AXValueGetValue(boundsValue, AccessibilityNativeMethods.kAXValueCGRectType, out var bounds))
                        {
                            _logger.Write(Tag, $"Insertion point bounds: X={bounds.X}, Y={bounds.Y}, W={bounds.Width}, H={bounds.Height}");
                            // Return the left edge of the character as the caret position
                            return new AccessibilityNativeMethods.CGRect { X = bounds.X, Y = bounds.Y, Width = 0, Height = bounds.Height };
                        }
                    }
                    finally
                    {
                        AccessibilityNativeMethods.CFRelease(boundsValue);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Write(Tag, $"Exception getting insertion point bounds: {ex.Message}");
                AccessibilityNativeMethods.CFRelease(axRangeValue);
            }
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception in GetCaretBoundsFromInsertionPoint: {ex.Message}");
        }

        _logger.Write(Tag, "Could not determine insertion point position");
        return null;
    }
}
