using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Commons;
using Microsoft.Extensions.DependencyInjection;

namespace SyncClipboard.Core.Clipboard;

public static class ContentControlHelper
{
    internal const string TextAttachmentMissing = "Skipped: Text attachment is missing.";

    private static SyncConfig GetSyncConfig() => AppCore.Current.Services.GetRequiredService<ConfigManager>().GetConfig<SyncConfig>();
    private static FileFilterConfig GetFileFilterConfig() => AppCore.Current.Services.GetRequiredService<ConfigManager>().GetConfig<FileFilterConfig>();

    public static async Task<string?> IsContentValid(Profile profile, CancellationToken token)
    {
        var syncConfig = GetSyncConfig();

        var typeValidationError = profile switch
        {
            TextProfile => ValidateTextUpload(syncConfig),
            ImageProfile => ValidateImageUpload(syncConfig),
            GroupProfile group => ValidateGroupUpload(group, syncConfig),
            FileProfile file => ValidateFileUpload(file, syncConfig),
            _ => null
        };

        if (typeValidationError != null)
            return typeValidationError;

        var sizeError = await ValidateSize(profile, syncConfig, token);
        if (sizeError is not null || profile is not TextProfile text)
            return sizeError;

        var filter = AppCore.Current.Services.GetRequiredService<ConfigManager>().GetConfig<TextFilterConfig>();
        try
        {
            return await IsTextAllowed(text, filter, token) ? null : I18n.Strings.TextUploadFiltered;
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return TextAttachmentMissing;
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is IOException or UnauthorizedAccessException)
        {
            return "Skipped: Text attachment could not be read.";
        }
    }

    internal static async Task<bool> IsTextAllowed(TextProfile profile, TextFilterConfig config, CancellationToken token)
    {
        var rules = config.FilterMode switch
        {
            "BlackList" => config.BlackList,
            "WhiteList" => config.WhiteList,
            _ => null,
        };
        if (rules is null || rules.Count == 0)
            return config.FilterMode != "WhiteList";

        return config.AllowsUpload(await profile.ReadFullText(token));
    }

    private static string? ValidateTextUpload(SyncConfig config) =>
        config.EnableUploadText ? null : "Skipped: Text upload is disabled in settings.";

    private static string? ValidateImageUpload(SyncConfig config) =>
        config.EnableUploadImage ? null : "Skipped: Image upload is disabled in settings.";

    private static string? ValidateGroupUpload(GroupProfile profile, SyncConfig config)
    {
        if (!IsFileAllowed(profile, GetFileFilterConfig()))
            return "Skipped: No valid files found in the group.";

        if (!config.EnableUploadMultiFile)
            return "Skipped: Multiple files upload is disabled in settings.";

        return null;
    }

    private static string? ValidateFileUpload(FileProfile profile, SyncConfig config)
    {
        if (!IsFileAllowed(profile, GetFileFilterConfig()))
            return $"Skipped: File '{profile.FileName}' is filtered out.";

        if (!config.EnableUploadSingleFile)
            return "Skipped: Single file upload is disabled in settings.";

        return null;
    }

    internal static bool IsFileAllowed(Profile profile, FileFilterConfig config) => profile switch
    {
        ImageProfile => true,
        GroupProfile group => group.Files.Any(name =>
            Directory.Exists(name) || FileFilterHelper.IsFileAvailableAfterFilter(name, config)),
        FileProfile file => FileFilterHelper.IsFileAvailableAfterFilter(file.FileName, config),
        _ => true,
    };

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
