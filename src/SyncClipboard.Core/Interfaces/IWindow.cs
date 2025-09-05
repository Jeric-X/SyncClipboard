namespace SyncClipboard.Core.Interfaces;

public interface IWindow
{
    void SwitchVisible();
    void Focus();
    void Close();
    void ScrollToTop() { }
}