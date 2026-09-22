using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using System.Text.Json;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryShortcutConfigTests
{
    [TestMethod]
    public void Defaults_PreserveExistingKeyboardAndMouseActions()
    {
        var config = new HistoryShortcutConfig();
        Assert.AreEqual(HistoryShortcutAction.CopyAndPaste, config.Match(new Hotkey(Key.Enter)));
        Assert.AreEqual(HistoryShortcutAction.Copy, config.Match(new Hotkey(Key.Alt, Key.Enter)));
        Assert.AreEqual(HistoryShortcutAction.ToggleStar, config.Match(new Hotkey(Key.Ctrl, Key.S)));
        Assert.AreEqual(HistoryShortcutAction.ToggleStarredFilter, config.Match(new Hotkey(Key.Ctrl, Key.Shift, Key.S)));
        Assert.AreEqual(HistoryShortcutAction.PreviousFilter, config.Match(new Hotkey(Key.Shift, Key.Tab)));
        Assert.AreEqual(HistoryMouseAction.Copy, config.DoubleClickAction);
        Assert.AreEqual(HistoryMouseAction.CopyAndPaste, config.MiddleClickAction);
        foreach (var action in Enum.GetValues<HistoryShortcutAction>())
            Assert.IsTrue(config.CanAssign(action, config.GetShortcut(action)));
    }

    [TestMethod]
    public void ReassigningShortcut_RemovesOldBindingAndRequiresExactModifiers()
    {
        var config = new HistoryShortcutConfig
        {
            Shortcuts = { [HistoryShortcutAction.Search] = new(Key.Alt, Key.F) }
        };
        Assert.IsNull(config.Match(new Hotkey(Key.Ctrl, Key.F)));
        Assert.AreEqual(HistoryShortcutAction.Search, config.Match(new Hotkey(Key.F, Key.Alt)));
        Assert.IsNull(config.Match(new Hotkey(Key.Ctrl, Key.Alt, Key.F)));
        Assert.IsNull(config.Match(new Hotkey(Key.Shift, Key.Enter)));
        Assert.IsNull(config.Match(new Hotkey(Key.Meta, Key.Down)));
    }

    [TestMethod]
    public void ClearedShortcut_DoesNotFallBackToDefault()
    {
        var config = new HistoryShortcutConfig
        {
            Shortcuts = { [HistoryShortcutAction.CopyAndPaste] = Hotkey.Nothing }
        };
        Assert.IsNull(config.Match(new Hotkey(Key.Enter)));
        Assert.IsNull(config.Match(Hotkey.Nothing));
        Assert.IsTrue(config.CanAssign(HistoryShortcutAction.Copy, new Hotkey(Key.Enter)));
    }

    [TestMethod]
    public void CloseShortcuts_CannotBeAssignedOrOverriddenByConfiguration()
    {
        var keys = new List<Hotkey> { new(Key.Esc), new(Key.Shift, Key.Esc), new(Key.Ctrl, Key.W) };
        if (OperatingSystem.IsMacOS())
            keys.Add(new(Key.Meta, Key.W));
        foreach (var hotkey in keys)
        {
            var config = new HistoryShortcutConfig
            {
                Shortcuts = { [HistoryShortcutAction.Copy] = hotkey }
            };
            Assert.IsFalse(config.CanAssign(HistoryShortcutAction.Copy, hotkey));
            Assert.IsNull(config.Match(hotkey));
        }
    }

    [TestMethod]
    public void Validation_RejectsDuplicatesModifierOnlyAndMultipleMainKeys()
    {
        var config = new HistoryShortcutConfig();
        Assert.IsFalse(config.CanAssign(HistoryShortcutAction.Copy, new Hotkey(Key.Ctrl, Key.F)));
        Assert.IsFalse(config.CanAssign(HistoryShortcutAction.Copy, new Hotkey(Key.Ctrl)));
        Assert.IsFalse(config.CanAssign(HistoryShortcutAction.Copy, new Hotkey(Key.Ctrl, Key.C, Key.V)));
        Assert.IsTrue(config.CanAssign(HistoryShortcutAction.Copy, new Hotkey(Key.F2)));
        Assert.IsTrue(config.CanAssign(HistoryShortcutAction.Copy, Hotkey.Nothing));
    }

    [TestMethod]
    [DataRow(Key.A)]
    [DataRow(Key.Z)]
    [DataRow(Key._0)]
    [DataRow(Key._9)]
    [DataRow(Key.NumPad0)]
    [DataRow(Key.NumPad9)]
    [DataRow(Key.Multiply)]
    [DataRow(Key.Add)]
    [DataRow(Key.Separator)]
    [DataRow(Key.Subtract)]
    [DataRow(Key.Decimal)]
    [DataRow(Key.Divide)]
    [DataRow(Key.NumPadEqual)]
    [DataRow(Key.Semicolon)]
    [DataRow(Key.Equal)]
    [DataRow(Key.Comma)]
    [DataRow(Key.Minus)]
    [DataRow(Key.Period)]
    [DataRow(Key.Slash)]
    [DataRow(Key.BackQuote)]
    [DataRow(Key.OpenBracket)]
    [DataRow(Key.BackSlash)]
    [DataRow(Key.CloshBracket)]
    [DataRow(Key.Quote)]
    [DataRow(Key.OEM_8)]
    [DataRow(Key.OEM_102)]
    [DataRow(Key.Underscore)]
    [DataRow(Key.Yen)]
    [DataRow(Key.JpComma)]
    public void SingleInputCharacter_CannotBeAssignedOrTriggeredFromExistingConfiguration(Key key)
    {
        var hotkey = new Hotkey(key);
        var config = new HistoryShortcutConfig
        {
            Shortcuts = { [HistoryShortcutAction.Copy] = hotkey }
        };

        Assert.IsFalse(config.CanAssign(HistoryShortcutAction.Copy, hotkey));
        Assert.IsNull(config.Match(hotkey));
    }

    [TestMethod]
    [DataRow(Key.Enter)]
    [DataRow(Key.NumPadReturn)]
    [DataRow(Key.Tab)]
    [DataRow(Key.Up)]
    [DataRow(Key.Down)]
    [DataRow(Key.Left)]
    [DataRow(Key.Right)]
    [DataRow(Key.Home)]
    [DataRow(Key.End)]
    [DataRow(Key.PgUp)]
    [DataRow(Key.PgDn)]
    [DataRow(Key.Backspace)]
    [DataRow(Key.Delete)]
    [DataRow(Key.Space)]
    [DataRow(Key.F1)]
    [DataRow(Key.F24)]
    public void OtherSingleKey_CanBeAssignedAndTriggered(Key key)
    {
        var config = new HistoryShortcutConfig
        {
            Shortcuts = Enum.GetValues<HistoryShortcutAction>().ToDictionary(action => action, _ => Hotkey.Nothing)
        };
        var hotkey = new Hotkey(key);

        Assert.IsTrue(config.CanAssign(HistoryShortcutAction.Copy, hotkey));
        config.Shortcuts[HistoryShortcutAction.Copy] = hotkey;
        Assert.AreEqual(HistoryShortcutAction.Copy, config.Match(hotkey));
    }

    [TestMethod]
    [DataRow(Key.Ctrl)]
    [DataRow(Key.Alt)]
    [DataRow(Key.Shift)]
    [DataRow(Key.Meta)]
    public void InputCharacterWithModifier_RemainsAssignableAndTakesPriority(Key modifier)
    {
        var config = new HistoryShortcutConfig();
        var hotkey = new Hotkey(modifier, Key.A);

        Assert.IsTrue(config.CanAssign(HistoryShortcutAction.Copy, hotkey));
        config.Shortcuts[HistoryShortcutAction.Copy] = hotkey;
        Assert.AreEqual(HistoryShortcutAction.Copy, config.Match(hotkey));
    }

    [TestMethod]
    public void PartialConfiguration_KeepsDefaultsForUnspecifiedActions()
    {
        var config = JsonSerializer.Deserialize<HistoryShortcutConfig>("""
            {"Shortcuts":{"Search":{"Keys":["Alt","F"]}},"DoubleClickAction":2}
            """)!;
        Assert.AreEqual(HistoryShortcutAction.Search, config.Match(new Hotkey(Key.Alt, Key.F)));
        Assert.AreEqual(HistoryShortcutAction.CopyAndPaste, config.Match(new Hotkey(Key.Enter)));
        Assert.AreEqual(HistoryMouseAction.CopyAndPaste, config.DoubleClickAction);
        Assert.AreEqual(HistoryMouseAction.CopyAndPaste, config.MiddleClickAction);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public void RemovedMouseActions_FallBackToDefaults(int action)
    {
        var config = JsonSerializer.Deserialize<HistoryShortcutConfig>(
            $$"""{"DoubleClickAction":{{action}},"MiddleClickAction":{{action}}}""")!;
        Assert.AreEqual(HistoryMouseAction.Copy, config.DoubleClickAction);
        Assert.AreEqual(HistoryMouseAction.CopyAndPaste, config.MiddleClickAction);
    }

    [TestMethod]
    public void Configuration_PersistsIndependentMouseActionsAndClearedShortcuts()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"HistoryShortcuts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var configurationServices = new ConfigurationTestServices();
            var path = Path.Combine(directory, "SyncClipboard.json");
            var manager = new ConfigManager(path, configurationServices.Upgrader);
            manager.SetConfig(new HistoryShortcutConfig
            {
                Shortcuts =
                {
                    [HistoryShortcutAction.Copy] = new(Key.Ctrl, Key.C),
                    [HistoryShortcutAction.CopyAndPaste] = Hotkey.Nothing
                },
                DoubleClickAction = HistoryMouseAction.CopyAndPaste,
                MiddleClickAction = HistoryMouseAction.Copy
            });
            var reloaded = new ConfigManager(path, configurationServices.Upgrader).GetConfig<HistoryShortcutConfig>();
            Assert.AreEqual(HistoryShortcutAction.Copy, reloaded.Match(new Hotkey(Key.Ctrl, Key.C)));
            Assert.IsNull(reloaded.Match(new Hotkey(Key.Enter)));
            Assert.AreEqual(HistoryMouseAction.CopyAndPaste, reloaded.DoubleClickAction);
            Assert.AreEqual(HistoryMouseAction.Copy, reloaded.MiddleClickAction);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void EquivalentConfigurations_DoNotPublishRedundantChanges()
    {
        var config = new HistoryShortcutConfig
        {
            Shortcuts = { [HistoryShortcutAction.Copy] = new(Key.F2) }
        };
        var roundTrip = JsonSerializer.Deserialize<HistoryShortcutConfig>(JsonSerializer.Serialize(config));
        Assert.AreEqual(config, roundTrip);
        Assert.AreEqual(config.GetHashCode(), roundTrip!.GetHashCode());
        Assert.AreNotEqual(config, config with { MiddleClickAction = HistoryMouseAction.Copy });
    }
}
