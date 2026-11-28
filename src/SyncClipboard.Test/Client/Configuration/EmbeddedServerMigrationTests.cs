using System.Text.Json.Nodes;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Server.Core.Services;

namespace SyncClipboard.Test.Client.Configuration;

[TestClass]
public class EmbeddedServerMigrationTests
{
    [TestMethod]
    public void UpgradeKeepsHostSettingsAndDoesNotReplaceLaterAccountChanges()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            const string old = """
                {"ServerService":{"Port":5999,"SwitchOn":true,"UserName":"alice","Password":"old-password",
                "MaxHistoryCount":77,"HistoryRetentionMinutes":222,"ProtectTaggedRecords":false,"ProtectTitledRecords":true}}
                """;
            var root = JsonNode.Parse(old)!.AsObject();
            var migration = new SyncClipboardConfigMigrationV3ToV4(dir.FullName);
            migration.Migrate(root);
            var host = root["ServerService"]!.AsObject();
            Assert.AreEqual(2, host.Count);
            Assert.AreEqual(5999, host["Port"]!.GetValue<int>());
            Assert.IsTrue(host["SwitchOn"]!.GetValue<bool>());
            var store = new ServerSettingsStore(dir.FullName, new(), null);
            Assert.AreEqual(ServerSettingsStore.AdministratorId, store.Authenticate("alice", "old-password")?.Id);
            Assert.AreEqual(77u, store.Value.MaxSavedHistoryCount);
            Assert.AreEqual(222u, store.Value.HistoryRetentionMinutes);
            Assert.IsFalse(store.Value.ProtectTaggedRecords);
            store.SetAccount(ServerSettingsStore.AdministratorId, new("renamed", "new-password"));

            migration.Migrate(JsonNode.Parse(old)!.AsObject());
            var restarted = new ServerSettingsStore(dir.FullName, new(), null);
            Assert.IsNull(restarted.Authenticate("alice", "old-password"));
            Assert.AreEqual(ServerSettingsStore.AdministratorId, restarted.Authenticate("renamed", "new-password")?.Id);
            var saved = File.ReadAllText(Path.Combine(dir.FullName, ServerSettingsStore.FileName));
            Assert.DoesNotContain("old-password", saved);
            Assert.DoesNotContain("new-password", saved);
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
