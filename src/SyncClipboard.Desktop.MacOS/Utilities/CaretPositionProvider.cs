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
    private static readonly IntPtr kAXFocusedUIElementAttribute = MacInteropHelper.CreateCFString("AXFocusedUIElement");
    private static readonly IntPtr kAXSelectedTextRangeAttribute = MacInteropHelper.CreateCFString("AXSelectedTextRange");
    private static readonly IntPtr kAXBoundsForRangeParameterizedAttribute = MacInteropHelper.CreateCFString("AXBoundsForRange");
    private static readonly IntPtr kAXManualAccessibilityAttribute = MacInteropHelper.CreateCFString("AXManualAccessibility");

    public ScreenPosition? GetCaretPosition()
    {
        if (!MacInterop.AXIsProcessTrusted())
        {
            _logger.Write(Tag, "Accessibility permission not granted");
            return null;
        }

        try
        {
            using var focusedElement = GetFocusedUIElement();
            if (focusedElement == null)
            {
                _logger.Write(Tag, "Failed to get focused UI element");
                return null;
            }

            var bounds = GetCaretBounds(focusedElement.Handle);
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
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
        }

        return null;
    }

    private CFHandle? GetFocusedUIElement()
    {
        // Enable accessibility for Electron apps (VSCode, Chrome, etc.)
        EnableAccessibilityForFrontmostApp();

        using var systemWide = MacInteropHelper.CreateSystemWide();
        if (systemWide.IsInvalid)
        {
            _logger.Write(Tag, "AXUIElementCreateSystemWide returned null");
            return null;
        }

        var focusedElement = MacInteropHelper.CopyAttributeValue(systemWide.Handle, kAXFocusedUIElementAttribute);
        if (focusedElement == null)
        {
            _logger.Write(Tag, "AXUIElementCopyAttributeValue failed to get focused element");
            return null;
        }

        return focusedElement;
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
            using var appElement = MacInteropHelper.CreateApplication(pid);
            if (appElement.IsInvalid)
            {
                return;
            }

            // Set AXManualAccessibility to true for Electron apps
            // NSNumber and CFBoolean are toll-free bridged
            using var trueValue = NSNumber.FromBoolean(true);
            var result = MacInterop.AXUIElementSetAttributeValue(appElement.Handle, kAXManualAccessibilityAttribute, trueValue.Handle);

            if (result == MacInterop.errAXSuccess)
            {
                _logger.Write(Tag, $"Enabled accessibility for: {frontmostApp.LocalizedName}");
            }
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception enabling accessibility: {ex.Message}");
        }
    }

    private MacInterop.CGRect? GetCaretBounds(IntPtr element)
    {
        // First, try to get selected text range
        using var textRangeValue = MacInteropHelper.CopyAttributeValue(element, kAXSelectedTextRangeAttribute);
        if (textRangeValue == null)
        {
            _logger.Write(Tag, "Failed to get selected text range");
            return null;
        }

        try
        {
            var bounds = GetCaretBoundsFromRange(element, textRangeValue.Handle);
            if (bounds.HasValue)
            {
                return bounds;
            }

            // If the above failed, try the insertion point method
            _logger.Write(Tag, "Trying insertion point method");
            return GetCaretBoundsFromInsertionPoint(element, textRangeValue.Handle);
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception in GetCaretBounds: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Get caret bounds from the selected text range.
    /// Returns null if bounds cannot be determined or insertion point method should be tried instead.
    /// </summary>
    private MacInterop.CGRect? GetCaretBoundsFromRange(IntPtr element, IntPtr textRangeValue)
    {
        var bounds = GetBoundsForRange(element, textRangeValue);
        if (bounds is null)
        {
            return null;
        }

        // Check if length == 0 and point is not on screen, we should try insertion point method instead
        var rangeType = MacInterop.AXValueGetType(textRangeValue);
        if (rangeType == MacInterop.kAXValueCFRangeType &&
            MacInterop.AXValueGetValueCFRange(textRangeValue, MacInterop.kAXValueCFRangeType, out var cfRange))
        {
            if (cfRange.Length == 0 && !IsPointOnScreen(bounds.Value.X, bounds.Value.Y))
            {
                _logger.Write(Tag, $"Length is 0 and point ({bounds.Value.X}, {bounds.Value.Y}) is not on screen, trying insertion point method");
                return null;
            }
        }

        return bounds;
    }

    /// <summary>
    /// Get bounds for a given range value.
    /// </summary>
    private MacInterop.CGRect? GetBoundsForRange(IntPtr element, IntPtr rangeValue)
    {
        using var boundsValue = MacInteropHelper.CopyParameterizedAttributeValue(element, kAXBoundsForRangeParameterizedAttribute, rangeValue);
        if (boundsValue == null)
        {
            _logger.Write(Tag, "Failed to get bounds for range");
            return null;
        }

        return ParseBoundsValue(boundsValue.Handle);
    }

    /// <summary>
    /// Parse bounds value from AXValue.
    /// Returns null if bounds cannot be determined.
    /// </summary>
    private MacInterop.CGRect? ParseBoundsValue(IntPtr boundsValue)
    {
        // Check the actual type of the AXValue
        var actualType = MacInterop.AXValueGetType(boundsValue);
        _logger.Write(Tag, $"AXValue actual type: {actualType} ({MacInterop.GetTypeName(actualType)})");

        MacInterop.CGRect? bounds = null;

        if (actualType == MacInterop.kAXValueCGRectType)
        {
            if (MacInterop.AXValueGetValue(boundsValue, MacInterop.kAXValueCGRectType, out var rect))
            {
                bounds = rect;
            }
        }
        else if (actualType == MacInterop.kAXValueCGPointType)
        {
            if (MacInterop.AXValueGetValuePoint(boundsValue, MacInterop.kAXValueCGPointType, out var point))
            {
                bounds = new MacInterop.CGRect { X = point.X, Y = point.Y, Width = 1, Height = 1 };
            }
        }

        if (bounds is null)
        {
            _logger.Write(Tag, "Can't get bounds value or type is not CGRect or CGPoint");
            return null;
        }
        _logger.Write(Tag, $"Raw bounds: X={bounds.Value.X}, Y={bounds.Value.Y}, W={bounds.Value.Width}, H={bounds.Value.Height}");

        return bounds;
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
    private MacInterop.CGRect? GetCaretBoundsFromInsertionPoint(IntPtr element, IntPtr textRangeValue)
    {
        try
        {
            var rangeType = MacInterop.AXValueGetType(textRangeValue);
            if (rangeType != MacInterop.kAXValueCFRangeType)
            {
                _logger.Write(Tag, $"TextRange is not CFRange: {rangeType}");
                return null;
            }

            if (!MacInterop.AXValueGetValueCFRange(textRangeValue, MacInterop.kAXValueCFRangeType, out var cfRange))
            {
                _logger.Write(Tag, "Failed to get CFRange value");
                return null;
            }

            _logger.Write(Tag, $"Insertion point at location: {cfRange.Location}, length: {cfRange.Length}");

            // Try to get bounds for a single character at the insertion point
            // by creating a CFRange with length 1
            var singleCharRange = new MacInterop.CFRange { Location = cfRange.Location, Length = 1 };
            using var axRangeValue = MacInteropHelper.CreateAXValueFromCFRange(singleCharRange);
            if (axRangeValue.IsInvalid)
            {
                _logger.Write(Tag, "Failed to create AXValue for single char range");
                return null;
            }

            var bounds = GetBoundsForRange(element, axRangeValue.Handle);
            if (bounds.HasValue)
            {
                // Return the left edge of the character as the caret position
                return new MacInterop.CGRect { X = bounds.Value.X, Y = bounds.Value.Y, Width = 0, Height = bounds.Value.Height };
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
