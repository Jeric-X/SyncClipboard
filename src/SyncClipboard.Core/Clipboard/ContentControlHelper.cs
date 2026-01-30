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

        var typeValidationError = profile switch
        {
            TextProfile => ValidateTextUpload(syncConfig),
            GroupProfile group => ValidateGroupUpload(group, syncConfig),
            FileProfile file => ValidateFileUpload(file, syncConfig),
            _ => null
        };

        if (typeValidationError != null)
            return typeValidationError;

        return await ValidateSize(profile, syncConfig, token);
    }

    private static string? ValidateTextUpload(SyncConfig config) =>
        config.EnableUploadText ? null : "Skipped: Text upload is disabled in settings.";

    private static string? ValidateGroupUpload(GroupProfile profile, SyncConfig config)
    {
        var hasValidFile = profile.Files?.Any(name =>
            Directory.Exists(name) ||
            FileFilterHelper.IsFileAvailableAfterFilter(name, GetFileFilterConfig())) ?? false;

        if (!hasValidFile)
            return "Skipped: No valid files found in the group.";

        if (!config.EnableUploadMultiFile)
            return "Skipped: Multiple files upload is disabled in settings.";

        return null;
    }

    private static string? ValidateFileUpload(FileProfile profile, SyncConfig config)
    {
        if (!FileFilterHelper.IsFileAvailableAfterFilter(profile.FileName, GetFileFilterConfig()))
            return $"Skipped: File '{profile.FileName}' is filtered out.";

        if (!config.EnableUploadSingleFile)
            return "Skipped: Single file upload is disabled in settings.";

        return null;
    }

    private static async Task<string?> ValidateSize(Profile profile, SyncConfig config, CancellationToken token)
    {
        var size = await profile.GetSize(token);
        if (size <= config.MaxFileByte)
            return null;

        var sizeMB = size / (1024.0 * 1024.0);
        var maxSizeMB = config.MaxFileByte / (1024.0 * 1024.0);
        return $"Skipped: File size {sizeMB:F2}MB exceeds limit {maxSizeMB:F0}MB.";
    }
}
