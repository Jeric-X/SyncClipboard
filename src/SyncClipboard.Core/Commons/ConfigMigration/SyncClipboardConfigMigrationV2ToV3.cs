using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using System.Text.Json.Nodes;

namespace SyncClipboard.Core.Commons.ConfigMigration;

public sealed class SyncClipboardConfigMigrationV2ToV3 : ISyncClipboardConfigMigration
{
    public int FromVersion => 2;
    public int ToVersion => 3;

    public void Migrate(JsonObject root)
    {
        if (root[HotkeyConfig.ConfigKey] is not JsonObject section
            || section[nameof(HotkeyConfig.Hotkeys)] is not JsonObject hotkeys)
            return;

        foreach (var (id, hotkey) in hotkeys.ToArray())
        {
            var commandId = CommandIds.Resolve(id);
            if (commandId == id)
                continue;

            // An explicitly configured readable name takes precedence over its legacy alias.
            if (!hotkeys.ContainsKey(commandId))
                hotkeys[commandId] = hotkey?.DeepClone();
            hotkeys.Remove(id);
        }
    }
}
