using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.WinUI3;

internal class AppConfig : IAppConfig
{
    public string AppId => Env.AppId;
    public string AppStringId => "SyncClipboard.WinUI";
    public string AppVersion => Core.Commons.Env.AppVersion;
    public string UpdateApiUrl => Core.Commons.Env.UpdateApiUrl;
    public string UpdateUrl => Core.Commons.Env.UpdateUrl;
}
