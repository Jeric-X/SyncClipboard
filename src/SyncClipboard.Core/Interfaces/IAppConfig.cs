namespace SyncClipboard.Core.Interfaces;

public interface IAppConfig
{
    string AppId { get; }
    string AppStringId { get; }
    string AppVersion { get; }
    string UpdateApiUrl { get; }
    string UpdateUrl { get; }
}
