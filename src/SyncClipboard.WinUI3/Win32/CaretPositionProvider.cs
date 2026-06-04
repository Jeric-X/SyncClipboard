using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SyncClipboard.WinUI3.Win32;

internal sealed class CaretPositionProvider(ILogger logger) : ICaretPositionProvider
{
    private readonly ILogger _logger = logger;
    private const string Tag = "CaretPosition";

    private Interop.UIAutomationClient.IUIAutomation? _uiAutomation;

    public ScreenPosition? GetCaretPosition()
    {
        try
        {
            var result = GetCaretPositionFromWin32();
            if (result != null)
            {
                return result;
            }

            _logger.Write(Tag, "Win32 method failed, trying MSAA");
            result = GetCaretPositionFromMSAA();
            if (result != null)
            {
                return result;
            }

            _logger.Write(Tag, "MSAA failed, trying UI Automation");
            return GetCaretPositionFromUIAutomation();
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return null;
        }
    }

    private ScreenPosition? GetCaretPositionFromWin32()
    {
        try
        {
            var info = new GUITHREADINFO
            {
                cbSize = Marshal.SizeOf<GUITHREADINFO>()
            };

            var foregroundWindow = User32Interop.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                _logger.Write(Tag, "GetForegroundWindow returned null");
                return null;
            }

            var threadId = User32Interop.GetWindowThreadProcessId(foregroundWindow, out _);

            if (!User32Interop.GetGUIThreadInfo((int)threadId, ref info))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.Write(Tag, $"GetGUIThreadInfo failed, error code: {error}");
                return null;
            }

            if (info.hwndCaret == IntPtr.Zero)
            {
                _logger.Write(Tag, "No caret window found (hwndCaret is null)");
                return null;
            }

            var point = new Point(info.rcCaret.Left, info.rcCaret.Top);
            if (!User32Interop.ClientToScreen(info.hwndCaret, ref point))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.Write(Tag, $"ClientToScreen failed, error code: {error}");
                return null;
            }

            var width = info.rcCaret.Right - info.rcCaret.Left;
            var height = info.rcCaret.Bottom - info.rcCaret.Top;

            _logger.Write(Tag, $"Win32 caret position: ({point.X}, {point.Y}), size: {width}x{height}");
            return new ScreenPosition
            {
                X = point.X,
                Y = point.Y,
                Width = width,
                Height = height
            };
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Win32 method exception: {ex.Message}");
            return null;
        }
    }

    private ScreenPosition? GetCaretPositionFromMSAA()
    {
        try
        {
            var foregroundWindow = User32Interop.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                _logger.Write(Tag, "MSAA: GetForegroundWindow returned null");
                return null;
            }

            var threadId = User32Interop.GetWindowThreadProcessId(foregroundWindow, out _);
            var info = new GUITHREADINFO
            {
                cbSize = Marshal.SizeOf<GUITHREADINFO>()
            };

            if (!User32Interop.GetGUIThreadInfo((int)threadId, ref info))
            {
                _logger.Write(Tag, "MSAA: GetGUIThreadInfo failed");
                return null;
            }

            var hwnd = info.hwndFocus != IntPtr.Zero ? info.hwndFocus : foregroundWindow;
            _logger.Write(Tag, $"MSAA: Using hwnd={hwnd.ToInt64():X}, hwndFocus={info.hwndFocus.ToInt64():X}");

            var iid = User32Interop.IID_IAccessible;
            var result = User32Interop.AccessibleObjectFromWindow(hwnd, User32Interop.OBJID_CARET, ref iid, out var accObject);
            if (result != 0 || accObject == null)
            {
                _logger.Write(Tag, $"AccessibleObjectFromWindow failed, result: {result}");
                return null;
            }

            try
            {
                var acc = (IAccessible)accObject;
                acc.accLocation(out var x, out var y, out var w, out var h, 0);

                if (x != 0 || y != 0)
                {
                    _logger.Write(Tag, $"MSAA caret position: ({x}, {y}), size: {w}x{h}");
                    return new ScreenPosition { X = x, Y = y, Width = w, Height = h };
                }
                _logger.Write(Tag, "MSAA accLocation returned (0,0), likely invalid");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Write(Tag, $"MSAA accLocation failed: {ex.Message}");
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"MSAA method exception: {ex.Message}");
            return null;
        }
    }

    private ScreenPosition? GetCaretPositionFromUIAutomation()
    {
        try
        {
            _uiAutomation ??= new Interop.UIAutomationClient.CUIAutomation8();
            if (_uiAutomation == null)
            {
                _logger.Write(Tag, "Failed to create UI Automation instance");
                return null;
            }

            var focusedElement = _uiAutomation.GetFocusedElement();
            if (focusedElement == null)
            {
                _logger.Write(Tag, "No focused element found");
                return null;
            }

            _logger.Write(Tag, $"Focused element: Name='{focusedElement.CurrentName}', ClassName='{focusedElement.CurrentClassName}', ControlType={focusedElement.CurrentControlType}");

            LogElementPatterns(focusedElement, "Focused");

            var result = TryGetCaretFromElement(focusedElement);
            if (result != null)
            {
                return result;
            }

            var legacyPattern = focusedElement.GetCurrentPattern(Interop.UIAutomationClient.UIA_PatternIds.UIA_LegacyIAccessiblePatternId);
            if (legacyPattern is Interop.UIAutomationClient.IUIAutomationLegacyIAccessiblePattern legacy)
            {
                _logger.Write(Tag, $"LegacyIAccessible: Name='{legacy.CurrentName}', Value='{legacy.CurrentValue}', Role={legacy.CurrentRole}, State={legacy.CurrentState}");
            }

            _logger.Write(Tag, "No suitable pattern found for caret position");
            return null;
        }
        catch (COMException comEx)
        {
            _logger.Write(Tag, $"UI Automation COM error: {comEx.Message}, HRESULT: {comEx.ErrorCode:X}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"UI Automation exception: {ex.Message}");
            return null;
        }
    }

    private void LogElementPatterns(Interop.UIAutomationClient.IUIAutomationElement element, string label)
    {
        try
        {
            var hasTextPattern2 = element.GetCurrentPropertyValue(Interop.UIAutomationClient.UIA_PropertyIds.UIA_IsTextPattern2AvailablePropertyId);
            var hasTextPattern = element.GetCurrentPropertyValue(Interop.UIAutomationClient.UIA_PropertyIds.UIA_IsTextPatternAvailablePropertyId);
            var hasLegacyPattern = element.GetCurrentPropertyValue(Interop.UIAutomationClient.UIA_PropertyIds.UIA_IsLegacyIAccessiblePatternAvailablePropertyId);
            _logger.Write(Tag, $"{label} patterns: TextPattern2={hasTextPattern2}, TextPattern={hasTextPattern}, LegacyIAccessible={hasLegacyPattern}");
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"LogElementPatterns failed: {ex.Message}");
        }
    }

    private ScreenPosition? TryGetCaretFromElement(Interop.UIAutomationClient.IUIAutomationElement element)
    {
        _logger.Write(Tag, $"TryGetCaretFromElement: Name='{element.CurrentName}', ClassName='{element.CurrentClassName}'");

        var pattern2 = element.GetCurrentPattern(Interop.UIAutomationClient.UIA_PatternIds.UIA_TextPattern2Id);
        _logger.Write(Tag, $"TextPattern2 pattern: {(pattern2 != null ? pattern2.GetType().Name : "null")}");
        if (pattern2 is Interop.UIAutomationClient.IUIAutomationTextPattern2 textPattern2)
        {
            try
            {
                var caretRange = textPattern2.GetCaretRange(out var isActive);
                if (caretRange != null)
                {
                    var rects = caretRange.GetBoundingRectangles();
                    if (rects != null && rects.Length >= 4)
                    {
                        var x = (int)rects[0];
                        var y = (int)rects[1];
                        var width = (int)rects[2];
                        var height = (int)rects[3];
                        _logger.Write(Tag, $"UI Automation (TextPattern2) caret position: ({x}, {y}), size: {width}x{height}, isActive: {isActive}");
                        return new ScreenPosition { X = x, Y = y, Width = width, Height = height };
                    }
                    _logger.Write(Tag, $"TextPattern2 GetCaretRange: rects.Length={rects?.Length ?? -1}");
                }
                else
                {
                    _logger.Write(Tag, "TextPattern2 GetCaretRange returned null");
                }
            }
            catch (Exception ex)
            {
                _logger.Write(Tag, $"TextPattern2 GetCaretRange failed: {ex.Message}");
            }
        }

        var pattern1 = element.GetCurrentPattern(Interop.UIAutomationClient.UIA_PatternIds.UIA_TextPatternId);
        _logger.Write(Tag, $"TextPattern pattern: {(pattern1 != null ? pattern1.GetType().Name : "null")}");
        if (pattern1 is Interop.UIAutomationClient.IUIAutomationTextPattern textPattern)
        {
            try
            {
                var selection = textPattern.GetSelection();
                _logger.Write(Tag, $"TextPattern GetSelection: selection={(selection != null ? selection.Length.ToString() : "null")}");
                if (selection != null && selection.Length > 0)
                {
                    var range = selection.GetElement(0);
                    var rects = range.GetBoundingRectangles();
                    _logger.Write(Tag, $"TextPattern range GetBoundingRectangles: rects.Length={rects?.Length ?? -1}");
                    if (rects != null && rects.Length >= 4)
                    {
                        var x = (int)rects[0];
                        var y = (int)rects[1];
                        var width = (int)rects[2];
                        var height = (int)rects[3];
                        _logger.Write(Tag, $"UI Automation (TextPattern selection) position: ({x}, {y}), size: {width}x{height}");
                        return new ScreenPosition { X = x, Y = y, Width = width, Height = height };
                    }

                    if (rects == null || rects.Length == 0)
                    {
                        _logger.Write(Tag, "TextPattern range is degenerate (empty), trying to expand...");
                        try
                        {
                            var expandedRange = range.Clone();
                            var moved = expandedRange.MoveEndpointByUnit(
                                Interop.UIAutomationClient.TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,
                                Interop.UIAutomationClient.TextUnit.TextUnit_Character,
                                1);
                            _logger.Write(Tag, $"Expanded range, moved={moved}");
                            if (moved > 0)
                            {
                                rects = expandedRange.GetBoundingRectangles();
                                _logger.Write(Tag, $"Expanded range GetBoundingRectangles: rects.Length={rects?.Length ?? -1}");
                                if (rects != null && rects.Length >= 4)
                                {
                                    var x = (int)rects[0];
                                    var y = (int)rects[1];
                                    var width = (int)rects[2];
                                    var height = (int)rects[3];
                                    _logger.Write(Tag, $"UI Automation (expanded range) position: ({x}, {y}), size: {width}x{height}");
                                    return new ScreenPosition { X = x, Y = y, Width = width, Height = height };
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Write(Tag, $"Expand range failed: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Write(Tag, $"TextPattern GetSelection failed: {ex.Message}");
            }
        }

        return null;
    }
}
