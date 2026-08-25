using System.Text.Json.Nodes;

namespace SyncClipboard.Core.Commons.ConfigMigration;

public sealed class SyncClipboardConfigMigrationV0ToV1 : ISyncClipboardConfigMigration
{
    private const string FileFilterKey = "FileFilter";

    public int FromVersion => 0;
    public int ToVersion => 1;

    public void Migrate(JsonObject root)
    {
        if (root[FileFilterKey] is null)
        {
            return;
        }

        if (root[FileFilterKey] is not JsonObject fileFilter)
        {
            throw new SyncClipboardConfigUpgradeException("FileFilter must be a JSON object.");
        }

        MigrateList(fileFilter, nameof(FileFilterConfig.WhiteList));
        MigrateList(fileFilter, nameof(FileFilterConfig.BlackList));
    }

    private static void MigrateList(JsonObject fileFilter, string propertyName)
    {
        if (fileFilter[propertyName] is null)
        {
            return;
        }

        if (fileFilter[propertyName] is not JsonArray legacyList)
        {
            throw new SyncClipboardConfigUpgradeException($"FileFilter.{propertyName} must be a JSON array.");
        }

        var migratedList = new JsonArray();
        foreach (var item in legacyList)
        {
            if (item is not JsonValue value || !value.TryGetValue<string>(out var pattern))
            {
                throw new SyncClipboardConfigUpgradeException(
                    $"FileFilter.{propertyName} contains a rule that cannot be upgraded from version 0.");
            }

            migratedList.Add(new JsonObject
            {
                [nameof(FileFilterRule.Pattern)] = pattern,
                [nameof(FileFilterRule.MatchMode)] = nameof(FileFilterMatchMode.Suffix),
            });
        }

        fileFilter[propertyName] = migratedList;
    }
}
