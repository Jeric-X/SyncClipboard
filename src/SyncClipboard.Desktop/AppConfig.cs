using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Desktop;

internal class AppConfig : IAppConfig
{
    public string AppId => Env.AppId;
    public string AppStringId => "SyncClipboard.Desktop";
    public string AppVersion => Core.Commons.Env.AppVersion;
    public string UpdateApiUrl => Core.Commons.Env.UpdateApiUrl;
    public string UpdateUrl => Core.Commons.Env.UpdateUrl;
}
