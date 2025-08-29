using NativeNotification.Interface;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.Commons;

public class ConfigManager : ConfigBase
{
    public ConfigManager(StaticConfig staticConfig, INotificationManager notification) : base(notification)
    {
        bool portableUserConfig = staticConfig.GetConfig<EnvConfig>().PortableUserConfig;
        SetPath(portableUserConfig);
        Load();
        staticConfig.ListenConfig<EnvConfig>(EnvConfigChanged);
    }

    private void EnvConfigChanged(EnvConfig envConfig)
    {
        SetPath(envConfig.PortableUserConfig);
        Save();
    }

    private void SetPath(bool portableUserConfig)
    {
        Path = portableUserConfig ? Env.PortableUserConfigFile : Env.UserConfigFile;
    }
}
