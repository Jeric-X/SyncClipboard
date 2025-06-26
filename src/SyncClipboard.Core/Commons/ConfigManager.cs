using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.Commons;

public class ConfigManager : ConfigBase
{
    public ConfigManager(StaticConfig staticConfig, INotification notification) : base(notification)
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

    public MenuItem[] Menu =>
    [
        new MenuItem(I18n.Strings.OpenConfigFile, () => Sys.OpenWithDefaultApp(Path)),
        new MenuItem(I18n.Strings.ReloadConfigFile, Load),
#if WINDOWS
        new MenuItem(I18n.Strings.OpenInstallFolder, () => Sys.OpenFolderInFileManager(Env.ProgramDirectory)),
#endif
        new MenuItem(I18n.Strings.OpenConfigFileFolder, () => Sys.OpenFolderInFileManager(Env.AppDataDirectory)),
    ];
}
