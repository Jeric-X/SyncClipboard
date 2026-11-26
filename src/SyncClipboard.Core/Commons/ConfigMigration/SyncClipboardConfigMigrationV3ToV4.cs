using System.Text.Json.Nodes;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services;

namespace SyncClipboard.Core.Commons.ConfigMigration;

public sealed class SyncClipboardConfigMigrationV3ToV4(string serverRoot) : ISyncClipboardConfigMigration
{
    public int FromVersion => 3;
    public int ToVersion => 4;

    public void Migrate(JsonObject root)
    {
        if (root[ServerConfig.ConfigKey] is not JsonObject section)
            return;

        if (!File.Exists(Path.Combine(serverRoot, ServerSettingsStore.FileName)))
        {
            var history = new AppSettings
            {
                MaxSavedHistoryCount = section["MaxHistoryCount"]?.GetValue<uint>() ?? 1000,
                HistoryRetentionMinutes = section["HistoryRetentionMinutes"]?.GetValue<uint>() ?? 0,
                ProtectTaggedRecords = section["ProtectTaggedRecords"]?.GetValue<bool>() ?? true,
                ProtectTitledRecords = section["ProtectTitledRecords"]?.GetValue<bool>() ?? true
            };
            var account = new AccountUpdate(section["UserName"]?.GetValue<string>() ?? "admin",
                section["Password"]?.GetValue<string>() ?? "admin");
            _ = new ServerSettingsStore(serverRoot, history, account);
        }
        foreach (var key in new[] { "UserName", "Password", "MaxHistoryCount", "HistoryRetentionMinutes", "ProtectTaggedRecords", "ProtectTitledRecords" })
            section.Remove(key);
    }
}
