using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IWindow
{
    bool IsVisible { get; }
    bool IsActive { get; }
    void Show(bool activate);
    void Hide();
    void CenterOnScreen(int width, int height);
    void SetTopmost(bool topmost);
    NativeWindowInfo? GetNativeWindowInfo();
    bool SetNearCaretPosition(ScreenPosition caretPosition);
    bool SetNearMousePosition(ScreenPosition mousePosition);
    bool SetPositionOnScreen(int screenX, int screenY);
}
