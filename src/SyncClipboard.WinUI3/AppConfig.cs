using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.WinUI3;

internal class AppConfig : IAppConfig
{
    public string AppId => Env.AppId;
    public string AppStringId => "SyncClipboard.WinUI";
    public string AppVersion => "2.8.3";
    public string UpdateApiUrl => "https://api.github.com/repos/Jeric-X/SyncClipboard/releases";
    public string UpdateUrl => "https://github.com/Jeric-X/SyncClipboard/releases/latest";
}
