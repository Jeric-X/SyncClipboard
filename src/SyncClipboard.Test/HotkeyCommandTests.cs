using Moq;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SyncClipboard.Test;

[TestClass]
public class HotkeyCommandTests
{
    private ConfigurationTestServices _services = null!;
    private string _directory = null!;
    private string _configPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _services = new ConfigurationTestServices();
        _directory = Path.Combine(Path.GetTempPath(), $"HotkeyCommandTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _configPath = Path.Combine(_directory, "SyncClipboard.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        _services.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    [DataRow("6DB18835-1DAD-0495-E126-45F5D2D193A7", "OpenMainUI")]
    [DataRow("2F30872E-B412-F580-7C20-F0D063A85BE0", "CompletelyExit")]
    [DataRow("26D8A39E-F50D-CC71-FE15-647F67FDB2F9", "SwitchClipboardSyncing")]
    [DataRow("145740F4-03F7-6F6C-5B93-B027C7C49C59", "SwitchBuiltInServer")]
    [DataRow("D0EDB9A4-3409-4A76-BC2B-4C0CD80DD850", "UploadOnce")]
    [DataRow("D13672E9-D14C-4D48-847E-10B030F4B608", "CopyAndUpload")]
    [DataRow("6C5314DF-B504-25EA-074D-396E5C69BAF1", "UploadWithoutFilter")]
    [DataRow("40E0B462-FCED-C4CD-7126-1F5204443DC1", "CopyAndUploadWithoutFilter")]
    [DataRow("95396FFF-E5FE-45D3-9D70-4A43FA34FF31", "DownloadOnce")]
    [DataRow("8a4a033e-31da-1b87-76ea-548885866b66", "DownloadAndPaste")]
    [DataRow("337275BE-57A2-2E97-6096-FF3D087D8A9C", "SwitchEasyCopyImage")]
    public void Upgrade_PreservesBindingAndSupportsBothCommandNames(string legacyId, string commandId)
    {
        var binding = new Hotkey(Key.Ctrl, Key.Shift, Key.U);
        WriteLegacyConfig(new() { [legacyId] = binding });
        var original = File.ReadAllText(_configPath);
        var config = new ConfigManager(_configPath, _services.Upgrader);
        var native = new Mock<INativeHotkeyRegistry>();
        native.Setup(x => x.IsValidHotkeyForm(binding)).Returns(true);
        Action? callback = null;
        native.Setup(x => x.RegisterForSystemHotkey(binding, It.IsAny<Action>()))
            .Callback<Hotkey, Action>((_, action) => callback = action).Returns(true);
        var manager = new HotkeyManager(native.Object, config);
        var calls = 0;
        manager.RegisterCommands(new UniqueCommandCollection("Test", "", new UniqueCommand("Test", commandId, () => calls++)));

        Assert.AreEqual(binding, config.GetConfig<HotkeyConfig>().Hotkeys[commandId]);
        Assert.HasCount(1, manager.HotkeyStatusMap);
        Assert.IsNotNull(callback);
        callback();
        manager.RunCommand(commandId);
        manager.RunCommand(legacyId);
        manager.RunCommand(legacyId.ToLowerInvariant());
        Assert.AreEqual(4, calls);
        native.Verify(x => x.RegisterForSystemHotkey(binding, It.IsAny<Action>()), Times.Once);

        var upgraded = JsonNode.Parse(File.ReadAllText(_configPath))!;
        Assert.AreEqual(Env.SyncClipboardConfigVersion, upgraded["ConfigVersion"]!.GetValue<int>());
        Assert.IsTrue(upgraded["Hotkey"]!["Hotkeys"]!.AsObject().ContainsKey(commandId));
        Assert.IsFalse(upgraded["Hotkey"]!["Hotkeys"]!.AsObject().ContainsKey(legacyId));
        var backup = Directory.GetFiles(Path.Combine(_directory, "config_backup"), "*.json").Single();
        Assert.AreEqual(original, File.ReadAllText(backup));

        manager.SetHotKey(commandId, Hotkey.Nothing);
        config.Reload();
        Assert.AreEqual(Hotkey.Nothing, config.GetConfig<HotkeyConfig>().Hotkeys[commandId]);
        manager.RunCommand(legacyId);
        Assert.AreEqual(5, calls);
        manager.SetHotKeyToDefault(commandId);
        manager.RunCommand(commandId);
        manager.RunCommand(legacyId);
        Assert.AreEqual(7, calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Upgrade_PrefersReadableNameAndPreservesDisabledAndUnknownBindings(bool readableNameFirst)
    {
        const string legacyId = "6DB18835-1DAD-0495-E126-45F5D2D193A7";
        var binding = new Hotkey(Key.Ctrl, Key.U);
        Dictionary<string, Hotkey> hotkeys = readableNameFirst
            ? new() { [CommandIds.OpenMainUI] = Hotkey.Nothing, [legacyId] = binding }
            : new() { [legacyId] = binding, [CommandIds.OpenMainUI] = Hotkey.Nothing };
        hotkeys["337275be-57a2-2e97-6096-ff3d087d8a9c"] = Hotkey.Nothing;
        hotkeys[CommandIds.OpenHistoryPanel] = binding;
        hotkeys["CustomCommand"] = binding;
        hotkeys["11111111-2222-3333-4444-555555555555"] = binding;
        WriteLegacyConfig(hotkeys);

        var config = new ConfigManager(_configPath, _services.Upgrader);
        var migrated = config.GetConfig<HotkeyConfig>().Hotkeys;

        Assert.HasCount(5, migrated);
        Assert.AreEqual(Hotkey.Nothing, migrated[CommandIds.OpenMainUI]);
        Assert.AreEqual(Hotkey.Nothing, migrated[CommandIds.SwitchEasyCopyImage]);
        Assert.AreEqual(binding, migrated[CommandIds.OpenHistoryPanel]);
        Assert.AreEqual(binding, migrated["CustomCommand"]);
        Assert.AreEqual(binding, migrated["11111111-2222-3333-4444-555555555555"]);
        var upgraded = File.ReadAllText(_configPath);
        config.Reload();
        Assert.AreEqual(upgraded, File.ReadAllText(_configPath));
        Assert.HasCount(1, Directory.GetFiles(Path.Combine(_directory, "config_backup"), "*.json"));
    }

    private void WriteLegacyConfig(Dictionary<string, Hotkey> hotkeys)
    {
        var root = new JsonObject
        {
            ["ConfigVersion"] = 2,
            [HotkeyConfig.ConfigKey] = JsonSerializer.SerializeToNode(new HotkeyConfig { Hotkeys = hotkeys }),
        };
        File.WriteAllText(_configPath, root.ToJsonString());
    }
}
