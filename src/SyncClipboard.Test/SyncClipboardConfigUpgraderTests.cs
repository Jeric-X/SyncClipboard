using SyncClipboard.Core.Commons.ConfigMigration;
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
        Assert.AreEqual(SyncClipboardConfigUpgrader.CurrentVersion, root[SyncClipboardConfigUpgrader.VersionPropertyName]!.GetValue<int>());
    }

    [TestMethod]
    public void Upgrade_MigratesLegacyFileFilterRulesToSuffixRules()
    {
        File.WriteAllText(_configPath, """
            {
              "FileFilter": {
                "FileFilterMode": "BlackList",
                "WhiteList": [".png"],
                "BlackList": [".tmp", ".log"]
              }
            }
            """);

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
    }

    [TestMethod]
    public void Upgrade_BacksUpCurrentConfigurationWithoutRewritingIt()
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
        var backup = Directory.EnumerateFiles(Path.Combine(_directory, "config_backup"), "*.json").Single();
        Assert.AreEqual(json, File.ReadAllText(backup));
    }

    [TestMethod]
    public void Upgrade_RejectsFutureConfigurationAndPreservesOriginal()
    {
        const string json = "{ \"ConfigVersion\": 99 }";
        File.WriteAllText(_configPath, json);

        Assert.ThrowsExactly<SyncClipboardConfigUpgradeException>(
            () => new SyncClipboardConfigUpgrader().Upgrade(_configPath));
        Assert.AreEqual(json, File.ReadAllText(_configPath));
        Assert.AreEqual(1, Directory.EnumerateFiles(Path.Combine(_directory, "config_backup"), "*.json").Count());
    }

    [TestMethod]
    public void Upgrade_RejectsMalformedJsonAfterBackingItUp()
    {
        const string json = "{ invalid";
        File.WriteAllText(_configPath, json);

        Assert.ThrowsExactly<SyncClipboardConfigUpgradeException>(
            () => new SyncClipboardConfigUpgrader().Upgrade(_configPath));
        Assert.AreEqual(json, File.ReadAllText(_configPath));
        Assert.AreEqual(1, Directory.EnumerateFiles(Path.Combine(_directory, "config_backup"), "*.json").Count());
    }

    [TestMethod]
    public void Upgrade_PrunesBackupsWhenConfigurationIsInvalid()
    {
        var upgrader = new SyncClipboardConfigUpgrader();
        for (var index = 0; index < 25; index++)
        {
            File.WriteAllText(_configPath, $"{{ invalid {index}");
            Assert.ThrowsExactly<SyncClipboardConfigUpgradeException>(() => upgrader.Upgrade(_configPath));
        }

        var backups = Directory
            .EnumerateFiles(Path.Combine(_directory, "config_backup"), "*.json")
            .ToArray();
        Assert.AreEqual(20, backups.Length);
        Assert.IsTrue(backups.Any(path => File.ReadAllText(path) == "{ invalid 24"));
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
    }

    private JsonObject ReadRoot() => JsonNode.Parse(File.ReadAllText(_configPath))!.AsObject();
}
