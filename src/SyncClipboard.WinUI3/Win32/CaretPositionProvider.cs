using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace SyncClipboard.WinUI3.Win32;

[ComImport]
[Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IAccessible
{
#pragma warning disable IDE1006 // 命名样式
    object accParent { get; }
    int accChildCount { get; }
    object accName(object varChild);
    object accValue(object varChild);
    object accDescription(object varChild);
    object accRole(object varChild);
    object accState(object varChild);
    object accHelp(object varChild);
    object accHelpTopic(out string pszHelpFile, object varChild);
    object accKeyboardShortcut(object varChild);
    object accFocus { get; }
    object accSelection { get; }
    object accLocation(out int pxLeft, out int pyTop, out int pcxWidth, out int pcyHeight, object varChild);
    object accNavigate(int navDir, object varStart);
    object accHitTest(int xLeft, int yTop);
    void accDoDefaultAction(object varChild);
#pragma warning restore IDE1006 // 命名样式
}

internal sealed class CaretPositionProvider(ILogger logger) : ICaretPositionProvider
{
    private readonly ILogger _logger = logger;
    private const string Tag = "CaretPosition";

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public Rectangle rcCaret;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(int idThread, ref GUITHREADINFO pgui);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hwnd, int dwObjectID, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);

    private const int OBJID_CARET = -8;
    private static readonly Guid IID_IAccessible = new(0x618736E0, 0x3C3D, 0x11CF, 0x81, 0x0C, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    private Interop.UIAutomationClient.IUIAutomation? _uiAutomation;

    public ScreenPosition GetCaretPosition()
    {
        try
        {
            var result = GetCaretPositionFromWin32();
            if (result.IsValid)
            {
                return result;
            }

            _logger.Write(Tag, "Win32 method failed, trying MSAA");
            result = GetCaretPositionFromMSAA();
            if (result.IsValid)
            {
                return result;
            }

            _logger.Write(Tag, "MSAA failed, trying UI Automation");
            return GetCaretPositionFromUIAutomation();
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
            return ScreenPosition.Invalid;
        }
    }

    private ScreenPosition GetCaretPositionFromWin32()
    {
        try
        {
            var info = new GUITHREADINFO
            {
                cbSize = Marshal.SizeOf<GUITHREADINFO>()
            };

            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                _logger.Write(Tag, "GetForegroundWindow returned null");
                return ScreenPosition.Invalid;
            }

            var threadId = GetWindowThreadProcessId(foregroundWindow, out _);

            if (!GetGUIThreadInfo((int)threadId, ref info))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.Write(Tag, $"GetGUIThreadInfo failed, error code: {error}");
                return ScreenPosition.Invalid;
            }

            if (info.hwndCaret == IntPtr.Zero)
            {
                _logger.Write(Tag, "No caret window found (hwndCaret is null)");
                return ScreenPosition.Invalid;
            }

            var point = new Point(info.rcCaret.Left, info.rcCaret.Top);
            if (!ClientToScreen(info.hwndCaret, ref point))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.Write(Tag, $"ClientToScreen failed, error code: {error}");
                return ScreenPosition.Invalid;
            }

            _logger.Write(Tag, $"Win32 caret position: ({point.X}, {point.Y})");
            return new ScreenPosition
            {
                X = point.X,
                Y = point.Y,
                IsValid = true
            };
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Win32 method exception: {ex.Message}");
            return ScreenPosition.Invalid;
        }
    }

    private ScreenPosition GetCaretPositionFromMSAA()
    {
        try
        {
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                _logger.Write(Tag, "MSAA: GetForegroundWindow returned null");
                return ScreenPosition.Invalid;
            }

            var threadId = GetWindowThreadProcessId(foregroundWindow, out _);
            var info = new GUITHREADINFO
            {
                cbSize = Marshal.SizeOf<GUITHREADINFO>()
            };

            if (!GetGUIThreadInfo((int)threadId, ref info))
            {
                _logger.Write(Tag, "MSAA: GetGUIThreadInfo failed");
                return ScreenPosition.Invalid;
            }

            var hwnd = info.hwndFocus != IntPtr.Zero ? info.hwndFocus : foregroundWindow;
            _logger.Write(Tag, $"MSAA: Using hwnd={hwnd.ToInt64():X}, hwndFocus={info.hwndFocus.ToInt64():X}");

            var iid = IID_IAccessible;
            var result = AccessibleObjectFromWindow(hwnd, OBJID_CARET, ref iid, out var accObject);
            if (result != 0 || accObject == null)
            {
                _logger.Write(Tag, $"AccessibleObjectFromWindow failed, result: {result}");
                return ScreenPosition.Invalid;
            }

            try
            {
                var acc = (IAccessible)accObject;
                acc.accLocation(out var x, out var y, out var w, out var h, 0);

                if (x != 0 || y != 0)
                {
                    _logger.Write(Tag, $"MSAA caret position: ({x}, {y}), size: {w}x{h}");
                    return new ScreenPosition { X = x, Y = y, IsValid = true };
                }
                _logger.Write(Tag, "MSAA accLocation returned (0,0), likely invalid");
                return ScreenPosition.Invalid;
            }
            catch (Exception ex)
            {
                _logger.Write(Tag, $"MSAA accLocation failed: {ex.Message}");
            }

            return ScreenPosition.Invalid;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"MSAA method exception: {ex.Message}");
            return ScreenPosition.Invalid;
        }
    }

    private ScreenPosition GetCaretPositionFromUIAutomation()
    {
        try
        {
            _uiAutomation ??= new Interop.UIAutomationClient.CUIAutomation8();
            if (_uiAutomation == null)
            {
                _logger.Write(Tag, "Failed to create UI Automation instance");
                return ScreenPosition.Invalid;
            }

            var focusedElement = _uiAutomation.GetFocusedElement();
            if (focusedElement == null)
            {
                _logger.Write(Tag, "No focused element found");
                return ScreenPosition.Invalid;
            }

            _logger.Write(Tag, $"Focused element: Name='{focusedElement.CurrentName}', ClassName='{focusedElement.CurrentClassName}', ControlType={focusedElement.CurrentControlType}");

            LogElementPatterns(focusedElement, "Focused");

            var result = TryGetCaretFromElement(focusedElement);
            if (result.IsValid)
            {
                return result;
            }

            result = TryGetCaretFromDescendantsWithPattern(focusedElement, Interop.UIAutomationClient.UIA_PatternIds.UIA_TextPattern2Id);
            if (result.IsValid)
            {
                return result;
            }

            result = TryGetCaretFromDescendantsWithPattern(focusedElement, Interop.UIAutomationClient.UIA_PatternIds.UIA_TextPatternId);
            if (result.IsValid)
            {
                return result;
            }

            result = TryGetCaretFromAllDescendants(focusedElement);
            if (result.IsValid)
            {
                return result;
            }

            var legacyPattern = focusedElement.GetCurrentPattern(Interop.UIAutomationClient.UIA_PatternIds.UIA_LegacyIAccessiblePatternId);
            if (legacyPattern is Interop.UIAutomationClient.IUIAutomationLegacyIAccessiblePattern legacy)
            {
                _logger.Write(Tag, $"LegacyIAccessible: Name='{legacy.CurrentName}', Value='{legacy.CurrentValue}', Role={legacy.CurrentRole}, State={legacy.CurrentState}");
            }

            _logger.Write(Tag, "No suitable pattern found for caret position");
            return ScreenPosition.Invalid;
        }
        catch (COMException comEx)
        {
            _logger.Write(Tag, $"UI Automation COM error: {comEx.Message}, HRESULT: {comEx.ErrorCode:X}");
            return ScreenPosition.Invalid;
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"UI Automation exception: {ex.Message}");
            return ScreenPosition.Invalid;
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

    private ScreenPosition TryGetCaretFromAllDescendants(Interop.UIAutomationClient.IUIAutomationElement element)
    {
        try
        {
            var trueCondition = _uiAutomation!.CreateTrueCondition();
            var descendants = element.FindAll(Interop.UIAutomationClient.TreeScope.TreeScope_Descendants, trueCondition);
            if (descendants != null && descendants.Length > 0)
            {
                _logger.Write(Tag, $"Found {descendants.Length} descendants, checking for TextPattern...");
                for (int i = 0; i < Math.Min(descendants.Length, 50); i++)
                {
                    var child = descendants.GetElement(i);
                    if (child == null) continue;

                    var hasTextPattern = child.GetCurrentPropertyValue(Interop.UIAutomationClient.UIA_PropertyIds.UIA_IsTextPatternAvailablePropertyId);
                    var hasTextPattern2 = child.GetCurrentPropertyValue(Interop.UIAutomationClient.UIA_PropertyIds.UIA_IsTextPattern2AvailablePropertyId);

                    if ((bool)hasTextPattern || (bool)hasTextPattern2)
                    {
                        _logger.Write(Tag, $"Descendant[{i}]: Name='{child.CurrentName}', ClassName='{child.CurrentClassName}', TextPattern={hasTextPattern}, TextPattern2={hasTextPattern2}");
                        var result = TryGetCaretFromElement(child);
                        if (result.IsValid)
                        {
                            return result;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"TryGetCaretFromAllDescendants failed: {ex.Message}");
        }

        return ScreenPosition.Invalid;
    }

    private ScreenPosition TryGetCaretFromElement(Interop.UIAutomationClient.IUIAutomationElement element)
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
                        _logger.Write(Tag, $"UI Automation (TextPattern2) caret position: ({x}, {y}), isActive: {isActive}");
                        return new ScreenPosition { X = x, Y = y, IsValid = true };
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
                        _logger.Write(Tag, $"UI Automation (TextPattern selection) position: ({x}, {y})");
                        return new ScreenPosition { X = x, Y = y, IsValid = true };
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
                                    _logger.Write(Tag, $"UI Automation (expanded range) position: ({x}, {y})");
                                    return new ScreenPosition { X = x, Y = y, IsValid = true };
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

        return ScreenPosition.Invalid;
    }

    private ScreenPosition TryGetCaretFromDescendantsWithPattern(Interop.UIAutomationClient.IUIAutomationElement element, int patternId)
    {
        try
        {
            var propertyId = patternId == Interop.UIAutomationClient.UIA_PatternIds.UIA_TextPattern2Id
                ? Interop.UIAutomationClient.UIA_PropertyIds.UIA_IsTextPattern2AvailablePropertyId
                : Interop.UIAutomationClient.UIA_PropertyIds.UIA_IsTextPatternAvailablePropertyId;

            var condition = _uiAutomation!.CreatePropertyCondition(propertyId, true);
            var descendants = element.FindAll(Interop.UIAutomationClient.TreeScope.TreeScope_Descendants, condition);
            if (descendants != null && descendants.Length > 0)
            {
                for (int i = 0; i < descendants.Length; i++)
                {
                    var child = descendants.GetElement(i);
                    if (child == null) continue;

                    var result = TryGetCaretFromElement(child);
                    if (result.IsValid)
                    {
                        _logger.Write(Tag, $"Found caret in descendant: Name='{child.CurrentName}', ClassName='{child.CurrentClassName}'");
                        return result;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"TryGetCaretFromDescendantsWithPattern failed: {ex.Message}");
        }

        return ScreenPosition.Invalid;
    }
}
