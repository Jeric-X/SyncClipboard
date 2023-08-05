using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Interfaces;

public interface IAppConfig
{
    public string RemoteProfilePath { get; }
    public string LocalTemplateFolder { get; }
    public string RemoteFileFolder { get; }
    public string UserConfigFile { get; }

    public ProgramConfig ProgramWideUserConfig { get; }
}
