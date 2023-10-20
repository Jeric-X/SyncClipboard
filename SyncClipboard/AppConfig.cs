using SyncClipboard.Core.Interfaces;

namespace SyncClipboard;

internal class AppConfig : IAppConfig
{
    public string AppId => Env.AppId;
}
