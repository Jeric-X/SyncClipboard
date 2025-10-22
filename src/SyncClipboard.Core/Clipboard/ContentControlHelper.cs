using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Commons;
using Microsoft.Extensions.DependencyInjection;

namespace SyncClipboard.Core.Clipboard;

public static class ContentControlHelper
{
    private static SyncConfig GetSyncConfig() => AppCore.Current.Services.GetRequiredService<ConfigManager>().GetConfig<SyncConfig>();
    private static FileFilterConfig GetFileFilterConfig() => AppCore.Current.Services.GetRequiredService<ConfigManager>().GetConfig<FileFilterConfig>();

    public static bool IsContentValid(Profile profile)
    {
        var syncConfig = GetSyncConfig();
        if (profile is TextProfile)
        {
            return syncConfig.EnableUploadText;
        }

        if (profile is GroupProfile groupProfile)
        {
            var hasItem = groupProfile.Files?.FirstOrDefault(name => Directory.Exists(name) || FileFilterHelper.IsFileAvailableAfterFilter(name, GetFileFilterConfig())) != null;
            return hasItem && syncConfig.EnableUploadMultiFile;
        }

        if (profile is FileProfile fileProfile)
        {
            return FileFilterHelper.IsFileAvailableAfterFilter(fileProfile.FullPath!, GetFileFilterConfig()) && syncConfig.EnableUploadSingleFile;
        }

        return false;
    }
}
