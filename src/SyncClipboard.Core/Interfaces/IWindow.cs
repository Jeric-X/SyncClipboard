using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IWindow
{
    bool IsVisible { get; }
    bool IsActive { get; }
    void Show(bool activate);
    void Hide();
    void CenterOnScreen(int width, int height);
    void FocusSearch();
    void ScrollToTop() { }
    void ScrollToSelectedItem() { }
    void SetTopmost(bool topmost) { }
    NativeWindowInfo? GetNativeWindowInfo() => null;
    bool GetScrollViewMetrics(out double offsetY, out double viewportHeight, out double extentHeight)
    {
        offsetY = 0; viewportHeight = 0; extentHeight = 0;
        return false;
    }

    bool SetNearCaretPosition(ScreenPosition caretPosition);
    bool SetNearMousePosition(ScreenPosition mousePosition);
    bool SetPositionOnScreen(int screenX, int screenY);
}
