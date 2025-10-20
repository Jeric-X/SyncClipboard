using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Commons;
using Microsoft.Extensions.DependencyInjection;

namespace SyncClipboard.Core.Clipboard;

public static class ContentControlHelper
{
    private static SyncConfig GetSyncConfig() => AppCore.Current.Services.GetRequiredService<ConfigManager>().GetConfig<SyncConfig>();
    private static FileFilterConfig GetFileFilterConfig() => AppCore.Current.Services.GetRequiredService<ConfigManager>().GetConfig<FileFilterConfig>();

    public static bool IsFileAvailableAfterFilter(string fileName, FileFilterConfig filterConfig)
    {
        if (filterConfig.FileFilterMode == "BlackList")
        {
            var str = filterConfig.BlackList.Find(str => fileName.EndsWith(str, StringComparison.OrdinalIgnoreCase));
            if (str is not null)
            {
                return false;
            }
        }
        else if (filterConfig.FileFilterMode == "WhiteList")
        {
            var str = filterConfig.WhiteList.Find(str => fileName.EndsWith(str, StringComparison.OrdinalIgnoreCase));
            if (str is null)
            {
                return false;
            }
        }
        return true;
    }

    public static bool IsContentValid(Profile profile)
    {
        var syncConfig = GetSyncConfig();
        if (profile is TextProfile)
        {
            return syncConfig.EnableUploadText;
        }

        if (profile is GroupProfile groupProfile)
        {
            var hasItem = groupProfile.Files?.FirstOrDefault(name => Directory.Exists(name) || IsFileAvailableAfterFilter(name, GetFileFilterConfig())) != null;
            return hasItem && syncConfig.EnableUploadMultiFile;
        }

        if (profile is FileProfile fileProfile)
        {
            return IsFileAvailableAfterFilter(fileProfile.FullPath!, GetFileFilterConfig()) && syncConfig.EnableUploadSingleFile;
        }

        return false;
    }
}
