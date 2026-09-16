using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using SyncClipboard.Core.RemoteServer.Adapter.S3Server;
using SyncClipboard.Core.RemoteServer.Adapter.WebDavServer;
using SyncClipboard.Shared.Attributes;
using SyncClipboard.Shared.Models;
using System.Reflection;

namespace SyncClipboard.Test;

[TestClass]
public class SyncClipboardConfigRegistryTests
{
    [TestMethod]
    public void GetDefaultKey_ReturnsConfigOwnedKeys()
    {
        Assert.AreEqual(ProgramConfig.ConfigKey, SyncClipboardConfigRegistry.GetDefaultKey<ProgramConfig>());
        Assert.AreEqual(SyncConfig.ConfigKey, SyncClipboardConfigRegistry.GetDefaultKey<SyncConfig>());
        Assert.AreEqual(ServerConfig.ConfigKey, SyncClipboardConfigRegistry.GetDefaultKey<ServerConfig>());
        Assert.AreEqual(FileFilterConfig.ConfigKey, SyncClipboardConfigRegistry.GetDefaultKey<FileFilterConfig>());
        Assert.AreEqual(EnvConfig.ConfigKey, SyncClipboardConfigRegistry.GetDefaultKey<EnvConfig>());
        Assert.AreEqual(RuntimeHistoryConfig.ConfigKey, SyncClipboardConfigRegistry.GetDefaultKey<RuntimeHistoryConfig>());
    }

    [TestMethod]
    public void ConfigRegistry_ScansBaseAndOptionalKeyAttributes()
    {
        Assert.HasCount(21, SyncClipboardConfigRegistry.Configurations);

        foreach (var registration in SyncClipboardConfigRegistry.Configurations)
        {
            var hasBaseKey = registration.ConfigType
                .GetCustomAttributes<ConfigKeyAttribute>()
                .Any(candidate =>
                    candidate.Key == registration.Key
                    && candidate.Storage == registration.Storage);
            var hasOptionalKey = registration.ConfigType
                .GetCustomAttributes<OptionalConfigKeyAttribute>()
                .Any(candidate =>
                    candidate.Key == registration.Key
                    && candidate.Storage == registration.Storage);

            Assert.IsTrue(hasBaseKey || hasOptionalKey);
        }

        Assert.AreEqual(
            ClipboardOwnerFilterConfig.ConfigKey,
            SyncClipboardConfigRegistry.GetDefaultKey<ClipboardOwnerFilterConfig>());
    }

    [TestMethod]
    public void AccountConfigRegistry_ScansKnownTypesAndPriorities()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                new AdapterConfigRegistration(OfficialConfig.ConfigTypeName, typeof(OfficialConfig), 1),
                new AdapterConfigRegistration(WebDavConfig.ConfigTypeName, typeof(WebDavConfig), 2),
                new AdapterConfigRegistration(S3Config.ConfigTypeName, typeof(S3Config), 3),
            },
            AccountConfigRegistry.Configurations.ToArray());
    }
}
