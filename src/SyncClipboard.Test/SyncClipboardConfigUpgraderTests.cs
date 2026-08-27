using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Shared.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SyncClipboard.Test;

[TestClass]
public class SyncClipboardConfigUpgraderTests
{
    private static readonly string[] ExpectedLegacyBlackList = [".tmp", ".log"];

    private string _directory = null!;
    private string _configPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"SyncClipboardConfigUpgraderTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _configPath = Path.Combine(_directory, "SyncClipboard.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void Upgrade_CreatesVersionedConfigOnFirstRun()
    {
        new SyncClipboardConfigUpgrader().Upgrade(_configPath);

        var root = ReadRoot();
        Assert.AreEqual(Env.SyncClipboardConfigVersion, root[SyncClipboardConfigUpgrader.VersionPropertyName]!.GetValue<int>());
    }

    [TestMethod]
    public void Upgrade_UsesDotPrefixedLockFile()
    {
        new SyncClipboardConfigUpgrader().Upgrade(_configPath);

        Assert.IsTrue(File.Exists(Path.Combine(_directory, ".SyncClipboard.json.upgrade.lock")));
        Assert.IsFalse(File.Exists(_configPath + ".upgrade.lock"));
    }

    [TestMethod]
    public void Upgrade_SupportsConfigPathWithoutDirectoryPart()
    {
        var fileName = $"SyncClipboardConfigUpgraderTests-{Guid.NewGuid():N}.json";
        var fullPath = Path.GetFullPath(fileName);
        var lockPath = Path.Combine(
            Path.GetDirectoryName(fullPath)!,
            $".{fileName}.upgrade.lock");

        try
        {
            new SyncClipboardConfigUpgrader().Upgrade(fileName);

            Assert.IsTrue(File.Exists(fullPath));
            Assert.IsTrue(File.Exists(lockPath));
        }
        finally
        {
            File.Delete(fullPath);
            File.Delete(lockPath);
        }
    }

    [TestMethod]
    public void Upgrade_MigratesLegacyFileFilterRulesToSuffixRules()
    {
        const string json = """
            {
              "FileFilter": {
                "FileFilterMode": "BlackList",
                "WhiteList": [".png"],
                "BlackList": [".tmp", ".log"]
              }
            }
            """;
        File.WriteAllText(_configPath, json);

        new SyncClipboardConfigUpgrader().Upgrade(_configPath);

        var root = ReadRoot();
        Assert.AreEqual(1, root[SyncClipboardConfigUpgrader.VersionPropertyName]!.GetValue<int>());

        var config = root["FileFilter"]!.Deserialize<FileFilterConfig>();
        Assert.IsNotNull(config);
        Assert.AreEqual(new FileFilterRule { Pattern = ".png", MatchMode = FileFilterMatchMode.Suffix }, config.WhiteList.Single());
        CollectionAssert.AreEqual(
            ExpectedLegacyBlackList,
            config.BlackList.Select(rule => rule.Pattern).ToArray());
        Assert.IsTrue(config.BlackList.All(rule => rule.MatchMode == FileFilterMatchMode.Suffix));

        var backup = Directory.EnumerateFiles(Path.Combine(_directory, "config_backup"), "*.json").Single();
        Assert.AreEqual(json, File.ReadAllText(backup));
    }

    [TestMethod]
    public void Upgrade_DoesNotBackUpOrRewriteCurrentConfiguration()
    {
        const string json = """
            {
              "ConfigVersion": 1,
              "Program": {
                "Language": "en-US"
              }
            }
            """;
        File.WriteAllText(_configPath, json);

        new SyncClipboardConfigUpgrader().Upgrade(_configPath);

        Assert.AreEqual(json, File.ReadAllText(_configPath));
        Assert.IsFalse(Directory.Exists(Path.Combine(_directory, "config_backup")));
    }

    [TestMethod]
    public void Upgrade_RejectsFutureConfigurationAndPreservesOriginal()
    {
        const string json = "{ \"ConfigVersion\": 99 }";
        File.WriteAllText(_configPath, json);

        Assert.ThrowsExactly<SyncClipboardConfigUpgradeException>(
            () => new SyncClipboardConfigUpgrader().Upgrade(_configPath));
        Assert.AreEqual(json, File.ReadAllText(_configPath));
        Assert.IsFalse(Directory.Exists(Path.Combine(_directory, "config_backup")));
    }

    [TestMethod]
    public void Upgrade_RejectsMalformedJsonWithoutBackingItUp()
    {
        const string json = "{ invalid";
        File.WriteAllText(_configPath, json);

        Assert.ThrowsExactly<SyncClipboardConfigUpgradeException>(
            () => new SyncClipboardConfigUpgrader().Upgrade(_configPath));
        Assert.AreEqual(json, File.ReadAllText(_configPath));
        Assert.IsFalse(Directory.Exists(Path.Combine(_directory, "config_backup")));
    }

    [TestMethod]
    public void Upgrade_RejectsMalformedKnownConfigurationSection()
    {
        const string json = """
            {
              "ConfigVersion": 1,
              "Program": "bad",
              "History": {
                "EnableHistory": true
              }
            }
            """;
        File.WriteAllText(_configPath, json);

        var exception = Assert.ThrowsExactly<SyncClipboardConfigUpgradeException>(
            () => new SyncClipboardConfigUpgrader().Upgrade(_configPath));

        Assert.Contains("Program", exception.Message);
        Assert.AreEqual(ProgramConfig.ConfigKey, exception.RecoverableSectionKey);
        Assert.AreEqual(json, File.ReadAllText(_configPath));
        Assert.IsFalse(Directory.Exists(Path.Combine(_directory, "config_backup")));
    }

    [TestMethod]
    public void Upgrade_RejectsNullHotkeyCollection()
    {
        const string json = """
            {
              "ConfigVersion": 1,
              "Hotkey": {
                "Hotkeys": null
              }
            }
            """;
        File.WriteAllText(_configPath, json);

        var exception = Assert.ThrowsExactly<SyncClipboardConfigUpgradeException>(
            () => new SyncClipboardConfigUpgrader().Upgrade(_configPath));

        Assert.Contains(HotkeyConfig.ConfigKey, exception.Message);
        Assert.AreEqual(json, File.ReadAllText(_configPath));
        Assert.IsFalse(Directory.Exists(Path.Combine(_directory, "config_backup")));
    }

    [TestMethod]
    public void Upgrade_RejectsNullStringInRegisteredConfiguration()
    {
        const string json = """
            {
              "ConfigVersion": 1,
              "Program": {
                "Language": null
              }
            }
            """;
        File.WriteAllText(_configPath, json);

        var exception = Assert.ThrowsExactly<SyncClipboardConfigUpgradeException>(
            () => new SyncClipboardConfigUpgrader().Upgrade(_configPath));

        Assert.Contains(ProgramConfig.ConfigKey, exception.Message);
        Assert.AreEqual(json, File.ReadAllText(_configPath));
    }

    [TestMethod]
    public void Upgrade_RejectsNullNestedCollectionItem()
    {
        const string json = """
            {
              "ConfigVersion": 1,
              "NetworkAccountSwitch": {
                "Rules": [null]
              }
            }
            """;
        File.WriteAllText(_configPath, json);

        var exception = Assert.ThrowsExactly<SyncClipboardConfigUpgradeException>(
            () => new SyncClipboardConfigUpgrader().Upgrade(_configPath));

        Assert.Contains(NetworkAccountSwitchConfig.ConfigKey, exception.Message);
        Assert.AreEqual(json, File.ReadAllText(_configPath));
    }

    [TestMethod]
    public void Upgrade_RejectsUndefinedEnumValue()
    {
        const string json = """
            {
              "ConfigVersion": 1,
              "NetworkAccountSwitch": {
                "NoMatchAction": 99
              }
            }
            """;
        File.WriteAllText(_configPath, json);

        var exception = Assert.ThrowsExactly<SyncClipboardConfigUpgradeException>(
            () => new SyncClipboardConfigUpgrader().Upgrade(_configPath));

        Assert.AreEqual(NetworkAccountSwitchConfig.ConfigKey, exception.RecoverableSectionKey);
        Assert.AreEqual(json, File.ReadAllText(_configPath));
    }

    [TestMethod]
    public void Upgrade_AcceptsEmptyHotkeyCollection()
    {
        const string json = """
            {
              "ConfigVersion": 1,
              "Hotkey": {
                "Hotkeys": {}
              }
            }
            """;
        File.WriteAllText(_configPath, json);

        new SyncClipboardConfigUpgrader().Upgrade(_configPath);

        Assert.AreEqual(json, File.ReadAllText(_configPath));
        Assert.IsFalse(Directory.Exists(Path.Combine(_directory, "config_backup")));
    }

    [TestMethod]
    public void Reload_InvalidSectionPreservesAndRestoresActiveConfiguration()
    {
        const string validJson = """
            {
              "ConfigVersion": 1,
              "Program": {
                "Language": "en-US"
              },
              "History": {
                "EnableHistory": false
              }
            }
            """;
        File.WriteAllText(_configPath, validJson);
        var manager = new ConfigManager(_configPath, new SyncClipboardConfigUpgrader());

        const string invalidJson = """
            {
              "ConfigVersion": 1,
              "Program": "bad",
              "History": {
                "EnableHistory": true
              }
            }
            """;
        File.WriteAllText(_configPath, invalidJson);

        var exception = Assert.ThrowsExactly<SyncClipboardConfigUpgradeException>(manager.Reload);
        Assert.AreEqual("en-US", manager.GetConfig<ProgramConfig>().Language);

        Assert.IsTrue(manager.RestoreCurrentConfig(exception.RecoverableSectionKey));
        var restored = JsonNode.Parse(File.ReadAllText(_configPath))!.AsObject();
        Assert.AreEqual("en-US", restored[ProgramConfig.ConfigKey]!["Language"]!.GetValue<string>());
        Assert.IsTrue(restored[HistoryConfig.ConfigKey]!["EnableHistory"]!.GetValue<bool>());

        manager.Reload();
        Assert.IsTrue(manager.GetConfig<HistoryConfig>().EnableHistory);
    }

    [TestMethod]
    public void Upgrade_RejectsMalformedSavedAccountConfiguration()
    {
        const string json = """
            {
              "ConfigVersion": 1,
              "SavedAccounts": {
                "WebDAV": {
                  "1": "bad"
                }
              }
            }
            """;
        File.WriteAllText(_configPath, json);

        var exception = Assert.ThrowsExactly<SyncClipboardConfigUpgradeException>(
            () => new SyncClipboardConfigUpgrader().Upgrade(_configPath));

        Assert.Contains("SavedAccounts.WebDAV.1", exception.Message);
        Assert.AreEqual(json, File.ReadAllText(_configPath));
    }

    [TestMethod]
    public void Upgrade_PrunesMigrationBackups()
    {
        var upgrader = new SyncClipboardConfigUpgrader();
        for (var index = 0; index < 25; index++)
        {
            File.WriteAllText(_configPath, $"{{ \"Marker\": {index} }}");
            upgrader.Upgrade(_configPath);
        }

        var backups = Directory
            .EnumerateFiles(Path.Combine(_directory, "config_backup"), "*.json")
            .ToArray();
        Assert.AreEqual(20, backups.Length);
        Assert.IsTrue(backups.Any(path => File.ReadAllText(path) == "{ \"Marker\": 24 }"));
    }

    [TestMethod]
    public void Upgrade_RejectsInvalidRegexInCurrentConfiguration()
    {
        const string json = """
            {
              "ConfigVersion": 1,
              "FileFilter": {
                "FileFilterMode": "BlackList",
                "WhiteList": [],
                "BlackList": [
                  {
                    "Pattern": "(",
                    "MatchMode": "Regex"
                  }
                ]
              }
            }
            """;
        File.WriteAllText(_configPath, json);

        Assert.ThrowsExactly<SyncClipboardConfigUpgradeException>(
            () => new SyncClipboardConfigUpgrader().Upgrade(_configPath));
        Assert.AreEqual(json, File.ReadAllText(_configPath));
        Assert.IsFalse(Directory.Exists(Path.Combine(_directory, "config_backup")));
    }

    [TestMethod]
    public void BackupConfig_CopiesInvalidConfigurationToBackupDirectory()
    {
        const string invalidJson = "{ invalid";
        File.WriteAllText(_configPath, invalidJson);

        var backupPath = SyncClipboardConfigUpgrader.BackupConfig(_configPath);

        Assert.IsNotNull(backupPath);
        Assert.AreEqual(invalidJson, File.ReadAllText(backupPath));
        Assert.AreEqual(invalidJson, File.ReadAllText(_configPath));
    }

    private JsonObject ReadRoot() => JsonNode.Parse(File.ReadAllText(_configPath))!.AsObject();
}
