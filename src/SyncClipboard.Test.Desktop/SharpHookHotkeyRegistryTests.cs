using Moq;
using SharpHook;
using SharpHook.Data;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Desktop.Utilities;

namespace SyncClipboard.Test.Desktop;

[TestClass]
[TestCategory("NonUI")]
public class SharpHookHotkeyRegistryTests
{
    [TestMethod]
    [DataRow(Key.Kanji, Key.Hanja)]
    [DataRow(Key.Hangul, Key.Kana)]
    public void LegacyAliasRegistration_UsesSameSlotAndCanBeRemovedThroughEitherName(Key legacy, Key canonical)
    {
        var hook = new Mock<IGlobalHook>();
        // This bypasses hook startup and permission checks. No events are raised and no UI dispatcher is used.
        hook.SetupGet(x => x.IsRunning).Returns(true);
        using var registry = new SharpHookHotkeyRegistry(hook.Object);
        var oldHotkey = new Hotkey(Key.Ctrl, legacy);
        var newHotkey = new Hotkey(Key.Ctrl, canonical);
        static void NeverInvoked() => Assert.Fail("A registration-only test must not dispatch a hotkey action.");

        Assert.IsTrue(registry.RegisterForSystemHotkey(oldHotkey, NeverInvoked));
        Assert.IsFalse(registry.RegisterForSystemHotkey(newHotkey, NeverInvoked));
        registry.UnRegisterForSystemHotkey(newHotkey);
        Assert.IsTrue(registry.RegisterForSystemHotkey(newHotkey, NeverInvoked));
        registry.UnRegisterForSystemHotkey(oldHotkey);
        Assert.IsTrue(registry.RegisterForSystemHotkey(oldHotkey, NeverInvoked));

        hook.Verify(x => x.Run(It.IsAny<GlobalHookType>()), Times.Never());
        hook.Verify(x => x.RunAsync(It.IsAny<GlobalHookType>(), It.IsAny<bool>()), Times.Never());
    }
}
