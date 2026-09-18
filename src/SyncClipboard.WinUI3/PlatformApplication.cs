using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.WinUI3;

internal sealed class PlatformApplication : IPlatformApplication
{
    public void Exit()
    {
        App.Current.ExitApplication();
    }

    public void SetFont(string font)
    {
    }
}
