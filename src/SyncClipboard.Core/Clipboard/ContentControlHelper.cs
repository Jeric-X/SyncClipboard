using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Commons;
using Microsoft.Extensions.DependencyInjection;

namespace SyncClipboard.Core.Clipboard;

public static class ContentControlHelper
{
    private static SyncConfig GetSyncConfig() => AppCore.Current.Services.GetRequiredService<ConfigManager>().GetConfig<SyncConfig>();
    private static FileFilterConfig GetFileFilterConfig() => AppCore.Current.Services.GetRequiredService<ConfigManager>().GetConfig<FileFilterConfig>();

    public static async Task<string?> IsContentValid(Profile profile, CancellationToken token)
    {
        var syncConfig = GetSyncConfig();
        if (profile is TextProfile)
        {
            if (!syncConfig.EnableUploadText)
            {
                return "Skipped: Text upload is disabled in settings.";
            }
        }
        else if (profile is GroupProfile groupProfile)
        {
            var hasItem = groupProfile.Files?.FirstOrDefault(name => Directory.Exists(name) || FileFilterHelper.IsFileAvailableAfterFilter(name, GetFileFilterConfig())) != null;
            if (!hasItem)
            {
                return "Skipped: No valid files found in the group.";
            }
            if (!syncConfig.EnableUploadMultiFile)
            {
                return "Skipped: Multiple files upload is disabled in settings.";
            }
        }
        else if (profile is FileProfile fileProfile)
        {
            if (!FileFilterHelper.IsFileAvailableAfterFilter(fileProfile.FileName, GetFileFilterConfig()))
            {
                return $"Skipped: File '{fileProfile.FileName}' is filtered out.";
            }
            if (!syncConfig.EnableUploadSingleFile)
            {
                return "Skipped: Single file upload is disabled in settings.";
            }
        }

        var profileSize = await profile.GetSize(token);
        if (profileSize > syncConfig.MaxFileByte)
        {
            var sizeMB = profileSize / (1024.0 * 1024.0);
            var maxSizeMB = syncConfig.MaxFileByte / (1024.0 * 1024.0);
            return $"Skipped: File size {sizeMB:F2}MB exceeds limit {maxSizeMB:F0}MB.";
        }

        return null;
    }
}
