using Moq;
using SharpHook;
using SharpHook.Data;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Desktop.Utilities;

namespace SyncClipboard.Test.Desktop;

[TestClass]
public class HotkeyInitializationTests
{
    [TestMethod]
    public void HookStartupFailure_IsLogged_AndShortcutIsNotReady()
    {
        var hook = new Mock<IGlobalHook>();
        var logger = new Mock<ILogger>();
        var failure = new HookException(UioHookResult.Failure, "Input device access denied");
        hook.Setup(x => x.RunAsync(GlobalHookType.Keyboard, true)).Returns(Task.FromException(failure));
        using var registry = new SharpHookHotkeyRegistry(hook.Object, logger.Object);

        var registered = registry.RegisterForSystemHotkey(new Hotkey(Key.Ctrl, Key.V), () => Assert.Fail());

        Assert.IsFalse(registered);
        hook.Verify(x => x.RunAsync(GlobalHookType.Keyboard, true), Times.Once);
        logger.Verify(x => x.Write("SharpHookHotkeyRegistry",
            It.Is<string>(message => message.Contains("Input device access denied"))), Times.Once);
    }

    [TestMethod]
    [DataRow(Key.Kanji, Key.Hanja)]
    [DataRow(Key.Hangul, Key.Kana)]
    public void LegacyAliases_ShareRegistrationAndRemovalWithCanonicalKeys(Key legacy, Key canonical)
    {
        var hook = new Mock<IGlobalHook>();
        hook.SetupGet(x => x.IsRunning).Returns(true);
        using var registry = new SharpHookHotkeyRegistry(hook.Object, Mock.Of<ILogger>());
        var savedHotkey = new Hotkey(Key.Ctrl, legacy);
        var reportedHotkey = new Hotkey(Key.Ctrl, canonical);

        Assert.IsTrue(registry.RegisterForSystemHotkey(savedHotkey, () => { }));
        Assert.IsFalse(registry.RegisterForSystemHotkey(reportedHotkey, () => { }));
        registry.UnRegisterForSystemHotkey(reportedHotkey);
        Assert.IsTrue(registry.RegisterForSystemHotkey(reportedHotkey, () => { }));
        registry.UnRegisterForSystemHotkey(savedHotkey);
        Assert.IsTrue(registry.RegisterForSystemHotkey(savedHotkey, () => { }));
    }
}
