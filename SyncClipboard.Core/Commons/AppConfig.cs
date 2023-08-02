using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Commons;

public class AppConfig : IAppConfig
{
    public string RemoteProfilePath => "SyncClipboard.json";
    public string RemoteFileFolder => "file";
    public string LocalTemplateFolder => Env.TemplateFileFolder;
    public string UserConfigFile => Env.FullPath("SyncClipboard.json");

    public ChangeableAppConfig ProgramWideUserConfig => new ChangeableAppConfig()
    {
        IntervalTime = 3,
        RetryTimes = 3,
        TimeOut = 100,
        Proxy = "",
        DeleteTempFilesOnStartUp = false,
        LogRemainDays = 8
    };
}
