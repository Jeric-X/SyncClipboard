using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Desktop;

internal class AppConfig : IAppConfig
{
    public string AppId => Env.AppId;
}
