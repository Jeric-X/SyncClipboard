using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Interfaces;

public interface IAppConfig
{
    public string RemoteProfilePath { get; }
    public string LocalTemplateFolder { get; }
    public string UserConfigFile { get; }
}
