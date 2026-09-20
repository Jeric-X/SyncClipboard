using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpHook;
using SharpHook.Data;
using SharpHook.Simulation;
using SyncClipboard.Core;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Utilities.Keyboard;

namespace SyncClipboard.Test;

[TestClass]
public class KeyboardSimulationTests
{
    private static readonly InputPermissionStatus Allowed = new(
        InputPermissionState.NotRequired, InputPermissionState.NotRequired, InputPermissionState.Available);
    private static readonly InputPermissionStatus Denied = Allowed with { InputSimulation = InputPermissionState.Denied };

    [TestMethod]
    public void ServiceContainer_ExposesOnlyWrapper_AndDoesNotCheckPermissionsDuringResolution()
    {
        var services = new ServiceCollection();
        AppCore.ConfigCommonService(services);
        var permissions = new Mock<IInputPermissionProvider>(MockBehavior.Strict);
        services.AddSingleton(permissions.Object);
        using var provider = services.BuildServiceProvider();

        var keyboard = provider.GetRequiredService<VirtualKeyboard>();

        Assert.AreSame(keyboard, provider.GetRequiredService<VirtualKeyboard>());
        Assert.IsNull(provider.GetService<IEventSimulator>());
        Assert.IsNull(provider.GetService<Lazy<IEventSimulator>>());
        permissions.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void DeniedPermission_DoesNotCreateSimulator_AndCanRecoverAfterGrant()
    {
        var permissions = new Mock<IInputPermissionProvider>();
        permissions.SetupSequence(x => x.GetStatus()).Returns(Denied).Returns(Allowed);
        var simulator = new Mock<IEventSimulator>();
        var created = 0;
        using var keyboard = new VirtualKeyboard(permissions.Object, () =>
        {
            created++;
            return simulator.Object;
        });

        Assert.ThrowsExactly<InvalidOperationException>(keyboard.Paste);
        Assert.AreEqual(0, created);
        keyboard.Paste();
        Assert.AreEqual(1, created);
    }

    [TestMethod]
    public void InitializationFailure_IsRetriedOnNextUse()
    {
        var permissions = CreatePermissions();
        var simulator = new Mock<IEventSimulator>();
        var created = 0;
        using var keyboard = new VirtualKeyboard(permissions.Object, () =>
        {
            if (++created == 1) throw new HookException(UioHookResult.ErrorLinuxOpenUinput);
            return simulator.Object;
        });

        Assert.ThrowsExactly<HookException>(keyboard.Paste);
        keyboard.Paste();
        Assert.AreEqual(2, created);
    }

    [TestMethod]
    public void CopyAndPaste_ReuseSimulator_AndServiceProviderDisposesIt()
    {
        var services = new ServiceCollection();
        AppCore.ConfigCommonService(services);
        var simulator = new Mock<IEventSimulator>();
        var events = new List<(KeyCode Key, bool Pressed)>();
        simulator.Setup(x => x.SimulateKeyPress(It.IsAny<KeyCode>()))
            .Callback<KeyCode>(key => events.Add((key, true))).Returns(UioHookResult.Success);
        simulator.Setup(x => x.SimulateKeyRelease(It.IsAny<KeyCode>()))
            .Callback<KeyCode>(key => events.Add((key, false))).Returns(UioHookResult.Success);
        var created = 0;
        services.AddSingleton(_ => new VirtualKeyboard(CreatePermissions().Object, () =>
        {
            created++;
            return simulator.Object;
        }));
        VirtualKeyboard keyboard;
        using (var provider = services.BuildServiceProvider())
        {
            keyboard = provider.GetRequiredService<VirtualKeyboard>();
            keyboard.Copy();
            keyboard.Paste();
            Assert.AreEqual(1, created);
            simulator.Verify(x => x.Dispose(), Times.Never);
        }

        var modifier = OperatingSystem.IsMacOS() ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;
        (KeyCode, bool)[] expected = [
            (modifier, true), (KeyCode.VcC, true), (KeyCode.VcC, false), (modifier, false),
            (modifier, true), (KeyCode.VcV, true), (KeyCode.VcV, false), (modifier, false)
        ];
        CollectionAssert.AreEqual(expected, events.ToArray());
        keyboard.Dispose();
        simulator.Verify(x => x.Dispose(), Times.Once);
        Assert.ThrowsExactly<ObjectDisposedException>(keyboard.Paste);
    }

    [TestMethod]
    public void SimulationError_ReleasesKeys_DisposesSimulator_AndAllowsRetry()
    {
        var simulator = new Mock<IEventSimulator>();
        simulator.Setup(x => x.SimulateKeyPress(KeyCode.VcV)).Returns(UioHookResult.ErrorAxApiDisabled);
        var replacement = new Mock<IEventSimulator>();
        var created = 0;
        using var keyboard = new VirtualKeyboard(CreatePermissions().Object,
            () => ++created == 1 ? simulator.Object : replacement.Object);

        var error = Assert.ThrowsExactly<HookException>(keyboard.Paste);

        Assert.AreEqual(UioHookResult.ErrorAxApiDisabled, error.Result);
        simulator.Verify(x => x.SimulateKeyRelease(KeyCode.VcV), Times.Once);
        var modifier = OperatingSystem.IsMacOS() ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;
        simulator.Verify(x => x.SimulateKeyRelease(modifier), Times.Once);
        simulator.Verify(x => x.Dispose(), Times.Once);
        keyboard.Paste();
        Assert.AreEqual(2, created);
    }

    [TestMethod]
    public void RevokedPermission_DisposesExistingSimulator_WithoutSendingMoreInput()
    {
        var permissions = new Mock<IInputPermissionProvider>();
        permissions.SetupSequence(x => x.GetStatus()).Returns(Allowed).Returns(Denied);
        var simulator = new Mock<IEventSimulator>();
        using var keyboard = new VirtualKeyboard(permissions.Object, () => simulator.Object);
        keyboard.Paste();
        simulator.Invocations.Clear();

        Assert.ThrowsExactly<InvalidOperationException>(keyboard.Paste);

        simulator.Verify(x => x.Dispose(), Times.Once);
        simulator.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void ReleaseKeys_UsesMappedKeys_AndEmptyHotkeyDoesNotInitialize()
    {
        var simulator = new Mock<IEventSimulator>();
        var created = 0;
        using var keyboard = new VirtualKeyboard(CreatePermissions().Object, () =>
        {
            created++;
            return simulator.Object;
        });
        keyboard.ReleaseKeys(Hotkey.Nothing);
        Assert.AreEqual(0, created);
        keyboard.ReleaseKeys(new Hotkey(Key.Hanja, Key.Kana, Key.OEM_102));
        simulator.Verify(x => x.SimulateKeyRelease(KeyCode.VcHanja), Times.Once);
        simulator.Verify(x => x.SimulateKeyRelease(KeyCode.VcKana), Times.Once);
        simulator.Verify(x => x.SimulateKeyRelease(KeyCode.VcSection), Times.Once);
        Assert.AreEqual(Key.OEM_102, KeyCodeMap.Map[KeyCode.VcSection]);
    }

    private static Mock<IInputPermissionProvider> CreatePermissions()
    {
        var permissions = new Mock<IInputPermissionProvider>();
        permissions.Setup(x => x.GetStatus()).Returns(Allowed);
        return permissions;
    }
}
