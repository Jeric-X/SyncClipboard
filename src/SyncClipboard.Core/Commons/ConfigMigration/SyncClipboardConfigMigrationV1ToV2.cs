using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SyncClipboard.Core.Commons.ConfigMigration;

public sealed class SyncClipboardConfigMigrationV1ToV2 : ISyncClipboardConfigMigration
{
    public int FromVersion => 1;
    public int ToVersion => 2;

    public void Migrate(JsonObject root)
    {
        if (root[ClipboardFactoryConfig.ConfigKey] is not JsonObject clipboard) return;

        if (!clipboard.ContainsKey(nameof(ClipboardFactoryConfig.ReadMethod)))
        {
            var prohibited = clipboard["ProhibitSources"]?.Deserialize<List<string>>() ?? [];
            // Preserve the first enabled reader in the old fallback order. An all-disabled list uses the default.
            var method = !prohibited.Contains("Avalonia") ? ClipboardReadMethod.Avalonia
                : !prohibited.Contains("xclip") ? ClipboardReadMethod.XClip
                : !prohibited.Contains("wl-clipboard") ? ClipboardReadMethod.WlClipboard
                : ClipboardReadMethod.Avalonia;
            clipboard[nameof(ClipboardFactoryConfig.ReadMethod)] = (int)method;
        }

        clipboard.Remove("ProhibitSources");
    }
}
