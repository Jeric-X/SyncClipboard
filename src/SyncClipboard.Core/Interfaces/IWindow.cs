namespace SyncClipboard.Core.Interfaces;

public interface IWindow
{
    void SwitchVisible();
    void Focus();
    void Close();
    void ScrollToTop() { }
    void ScrollToSelectedItem() { }
    void SetTopmost(bool topmost) { }
    bool GetScrollViewMetrics(out double offsetY, out double viewportHeight, out double extentHeight)
    {
        offsetY = 0; viewportHeight = 0; extentHeight = 0;
        return false;
    }
}