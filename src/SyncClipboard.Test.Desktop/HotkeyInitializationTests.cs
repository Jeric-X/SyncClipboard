using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpHook;
using SharpHook.Data;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Desktop;

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
        var services = AppServices.ConfigureServices();
        services.AddSingleton(hook.Object);
        services.AddSingleton(logger.Object);
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<INativeHotkeyRegistry>();

        var registered = registry.RegisterForSystemHotkey(new Hotkey(Key.Ctrl, Key.V), () => Assert.Fail());

        Assert.IsFalse(registered);
        hook.Verify(x => x.RunAsync(GlobalHookType.Keyboard, true), Times.Once);
        logger.Verify(x => x.Write("SharpHookHotkeyRegistry",
            It.Is<string>(message => message.Contains("Input device access denied"))), Times.Once);
    }
}
