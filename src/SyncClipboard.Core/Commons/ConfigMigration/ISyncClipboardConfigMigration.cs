using System.Text.Json.Nodes;

namespace SyncClipboard.Core.Commons.ConfigMigration;

public interface ISyncClipboardConfigMigration
{
    int FromVersion { get; }
    int ToVersion { get; }

    void Migrate(JsonObject root);
}
