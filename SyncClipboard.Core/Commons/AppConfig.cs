using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Commons;

public class AppConfig : IAppConfig
{
    public string RemoteProfilePath => "SyncClipboard.json";
    public string RemoteFileFolder => "file";
    public string LocalTemplateFolder => Env.TemplateFileFolder;
}
