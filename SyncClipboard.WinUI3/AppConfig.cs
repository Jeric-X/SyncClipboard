using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.WinUI3;

internal class AppConfig : IAppConfig
{
    public string AppId => Env.AppId;
}
