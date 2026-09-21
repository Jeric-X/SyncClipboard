using Microsoft.Extensions.DependencyInjection;
using Moq;
using NativeNotification.Interface;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SyncClipboard.Test;

[TestClass]
public class ClipboardSettingsTests
{
    [TestMethod]
    [DataRow("[]", ClipboardReadMethod.Avalonia)]
    [DataRow("[\"Avalonia\"]", ClipboardReadMethod.XClip)]
    [DataRow("[\"Avalonia\",\"xclip\"]", ClipboardReadMethod.WlClipboard)]
    [DataRow("[\"xclip\",\"wl-clipboard\"]", ClipboardReadMethod.Avalonia)]
    [DataRow("[\"Avalonia\",\"xclip\",\"wl-clipboard\"]", ClipboardReadMethod.Avalonia)]
    public void LegacySourcesMigrateToFirstEnabledReader(string prohibited, ClipboardReadMethod expected)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "config.json");
            var legacy = new JsonObject
            {
                ["ConfigVersion"] = 1,
                ["ClipboardFactory"] = new JsonObject { ["ProhibitSources"] = JsonNode.Parse(prohibited) }
            };
            File.WriteAllText(path, legacy.ToJsonString());
            var config = new ConfigManager(path, new SyncClipboardConfigUpgrader()).GetConfig<ClipboardFactoryConfig>();
            Assert.AreEqual(expected, config.ReadMethod);
            Assert.AreEqual(ClipboardWriteMethod.Avalonia, config.WriteMethod);
            Assert.IsFalse(JsonNode.Parse(File.ReadAllText(path))!["ClipboardFactory"]!.AsObject().ContainsKey("ProhibitSources"));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void MigrationPreservesExplicitReadAndWriteMethods()
    {
        var root = JsonNode.Parse("""
            {"ClipboardFactory":{"ReadMethod":2,"WriteMethod":1,"ProhibitSources":["wl-clipboard"]}}
            """)!.AsObject();
        new SyncClipboardConfigMigrationV1ToV2().Migrate(root);
        var config = root["ClipboardFactory"]!.Deserialize<ClipboardFactoryConfig>()!;
        Assert.AreEqual(ClipboardReadMethod.WlClipboard, config.ReadMethod);
        Assert.AreEqual(ClipboardWriteMethod.WlClipboard, config.WriteMethod);
    }

    [TestMethod]
    public void SystemSettingsPersistIndependentReadAndWriteSelections()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "config.json");
            var config = new ConfigManager(path, new SyncClipboardConfigUpgrader());
            Assert.AreEqual(ClipboardReadMethod.Avalonia, config.GetConfig<ClipboardFactoryConfig>().ReadMethod);
            config.SetConfig(new ClipboardFactoryConfig { ReadMethod = ClipboardReadMethod.WlClipboard });
            using var services = new ServiceCollection().AddSingleton(Mock.Of<ILogger>()).BuildServiceProvider();
            var vm = new SystemSettingViewModel(config, new StaticConfig(Mock.Of<INotificationManager>()), services);

            Assert.AreEqual(ClipboardReadMethod.WlClipboard, vm.ClipboardReadingMethod.Key);
            vm.ClipboardWritingMethod = SystemSettingViewModel.ClipboardWriteMethods[1];
            Assert.AreEqual(ClipboardReadMethod.WlClipboard, config.GetConfig<ClipboardFactoryConfig>().ReadMethod);

            vm.ClipboardReadingMethod = SystemSettingViewModel.ClipboardReadMethods[1];
            var saved = new ConfigManager(path, new SyncClipboardConfigUpgrader()).GetConfig<ClipboardFactoryConfig>();
            Assert.AreEqual(ClipboardReadMethod.XClip, saved.ReadMethod);
            Assert.AreEqual(ClipboardWriteMethod.WlClipboard, saved.WriteMethod);

            config.SetConfig(saved with { ReadMethod = ClipboardReadMethod.Avalonia, WriteMethod = ClipboardWriteMethod.Avalonia });
            Assert.AreEqual(ClipboardReadMethod.Avalonia, vm.ClipboardReadingMethod.Key);
            Assert.AreEqual(ClipboardWriteMethod.Avalonia, vm.ClipboardWritingMethod.Key);
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
