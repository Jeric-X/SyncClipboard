using NativeNotification.Interface;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Commons;

public class ConfigManager : ConfigBase
{
    private readonly SyncClipboardConfigUpgrader _configUpgrader;

    public ConfigManager(
        StaticConfig staticConfig,
        INotificationManager notification,
        SyncClipboardConfigUpgrader configUpgrader) : base(notification)
    {
        _configUpgrader = configUpgrader;
        bool portableUserConfig = staticConfig.GetConfig<EnvConfig>().PortableUserConfig;
        SetPath(portableUserConfig);
        Reload();
        staticConfig.ListenConfig<EnvConfig>(EnvConfigChanged);
    }

    public void Reload()
    {
        _configUpgrader.Upgrade(Path);
        Load();
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
