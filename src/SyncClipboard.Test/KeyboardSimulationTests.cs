using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpHook;
using SharpHook.Data;
using SharpHook.Simulation;
using SyncClipboard.Core;
using SyncClipboard.Core.I18n;
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
    public void AutomaticAccessibilityRequest_ResetsBeforeRequesting_AndSkipsTriggeringInput()
    {
        var events = new List<string>();
        var status = Denied with { Accessibility = InputPermissionState.Denied };
        var provider = new InputPermissionProvider(Mock.Of<ILogger>(), () =>
        {
            events.Add("reset");
            return Task.CompletedTask;
        }, () =>
        {
            events.Add("request");
            status = Allowed;
        }, isAccessibilityEnabled: () => false);
        var permissions = new Mock<IInputPermissionProvider>();
        permissions.Setup(x => x.GetSimulationStatus()).Returns(() => status);
        permissions.Setup(x => x.CheckAndRequestAccessibilityPermission()).Returns(provider.CheckAndRequestAccessibilityPermission);
        var simulator = new Mock<IEventSimulator>();
        using var keyboard = new VirtualKeyboard(permissions.Object, () =>
        {
            events.Add("create simulator");
            return simulator.Object;
        });

        var error = Assert.ThrowsExactly<InvalidOperationException>(keyboard.Paste);
        Assert.AreEqual(Strings.InputSimulationPermissionRequired, error.Message);
        simulator.Verify(x => x.SimulateKeyPress(It.IsAny<KeyCode>()), Times.Never);
        keyboard.Paste();

        string[] expected = ["reset", "request", "create simulator"];
        CollectionAssert.AreEqual(expected, events);
        permissions.Verify(x => x.CheckAndRequestAccessibilityPermission(), Times.Once);
        simulator.Verify(x => x.SimulateKeyPress(KeyCode.VcV), Times.Once);
    }

    [TestMethod]
    public void ServiceContainer_ExposesOnlyWrapper_AndDoesNotCheckPermissionsDuringResolution()
    {
        var services = new ServiceCollection();
        AppCore.ConfigCommonService(services);
        var permissions = new Mock<IInputPermissionProvider>(MockBehavior.Strict);
        services.AddSingleton(permissions.Object);
        services.AddSingleton(Mock.Of<IThreadDispatcher>());
        services.AddSingleton(Mock.Of<IGlobalDialog>());
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
        permissions.SetupSequence(x => x.GetSimulationStatus()).Returns(Denied).Returns(Allowed);
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
    [DataRow(InputPermissionState.Denied)]
    [DataRow(InputPermissionState.NotRequired)]
    public void MissingPermission_PromptsOnce_AndAllowsRetryAfterGrant(InputPermissionState accessibility)
    {
        var status = Denied with { Accessibility = accessibility };
        var permissions = new Mock<IInputPermissionProvider>();
        permissions.Setup(x => x.GetSimulationStatus()).Returns(() => status);
        var simulator = new Mock<IEventSimulator>();
        var created = 0;
        var notices = 0;
        using var keyboard = new VirtualKeyboard(permissions.Object, () =>
        {
            created++;
            return simulator.Object;
        }, () => notices++);

        keyboard.SendShortcut();
        keyboard.ReleaseKeys(Hotkey.Nothing);
        permissions.VerifyNoOtherCalls();
        Assert.AreEqual(0, notices);

        Assert.ThrowsExactly<InvalidOperationException>(keyboard.Copy);
        Assert.ThrowsExactly<InvalidOperationException>(keyboard.Paste);
        Assert.ThrowsExactly<InvalidOperationException>(() => keyboard.ReleaseKeys(new Hotkey(Key.Hanja)));
        Assert.AreEqual(0, created);
        permissions.Verify(x => x.CheckAndRequestAccessibilityPermission(),
            accessibility == InputPermissionState.NotRequired ? Times.Never() : Times.Once());
        Assert.AreEqual(accessibility == InputPermissionState.NotRequired ? 1 : 0, notices);

        status = Allowed;
        keyboard.Paste();
        Assert.AreEqual(1, created);

        status = Denied with { Accessibility = accessibility };
        Assert.ThrowsExactly<InvalidOperationException>(keyboard.Paste);
        permissions.Verify(x => x.CheckAndRequestAccessibilityPermission(),
            accessibility == InputPermissionState.NotRequired ? Times.Never() : Times.Once());
        Assert.AreEqual(accessibility == InputPermissionState.NotRequired ? 1 : 0, notices);
    }

    [TestMethod]
    public void AccessibilityRequest_DoesNotRepeatAfterFailure()
    {
        var permissions = new Mock<IInputPermissionProvider>();
        permissions.Setup(x => x.GetSimulationStatus()).Returns(Denied with { Accessibility = InputPermissionState.Denied });
        var provider = new InputPermissionProvider(Mock.Of<ILogger>(), () => Task.CompletedTask,
            () => throw new InvalidOperationException("Request failed"), isAccessibilityEnabled: () => false);
        permissions.Setup(x => x.CheckAndRequestAccessibilityPermission()).Returns(provider.CheckAndRequestAccessibilityPermission);
        var simulator = new Mock<IEventSimulator>();
        using var keyboard = new VirtualKeyboard(permissions.Object, () => simulator.Object);

        var error = Assert.ThrowsExactly<InvalidOperationException>(keyboard.Copy);
        Assert.AreEqual(Strings.InputSimulationPermissionRequired, error.Message);
        Assert.ThrowsExactly<InvalidOperationException>(keyboard.Copy);
        permissions.Verify(x => x.CheckAndRequestAccessibilityPermission(), Times.Once);
        simulator.Verify(x => x.SimulateKeyPress(It.IsAny<KeyCode>()), Times.Never);
    }

    [TestMethod]
    public void PendingAccessibilityRequest_SkipsInputWithoutWaiting()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var permissions = new Mock<IInputPermissionProvider>();
        permissions.Setup(x => x.GetSimulationStatus()).Returns(Denied with { Accessibility = InputPermissionState.Denied });
        var provider = new InputPermissionProvider(Mock.Of<ILogger>(), () => completion.Task,
            () => { }, isAccessibilityEnabled: () => false);
        permissions.Setup(x => x.CheckAndRequestAccessibilityPermission()).Returns(provider.CheckAndRequestAccessibilityPermission);
        var simulator = new Mock<IEventSimulator>();
        using var keyboard = new VirtualKeyboard(permissions.Object, () => simulator.Object);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                keyboard.Paste();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { IsBackground = true };

        try
        {
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "Input waited for the permission request.");
            Assert.IsInstanceOfType<InvalidOperationException>(failure);
            Assert.AreEqual(Strings.InputSimulationPermissionRequired, failure.Message);
            Assert.ThrowsExactly<InvalidOperationException>(keyboard.Copy);
            permissions.Verify(x => x.CheckAndRequestAccessibilityPermission(), Times.Once);
            simulator.Verify(x => x.SimulateKeyPress(It.IsAny<KeyCode>()), Times.Never);
        }
        finally
        {
            completion.TrySetResult();
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public void AccessibilityRequest_StartsOutsideKeyboardLock()
    {
        var permissions = new Mock<IInputPermissionProvider>();
        permissions.Setup(x => x.GetSimulationStatus()).Returns(Denied with { Accessibility = InputPermissionState.Denied });
        using var keyboard = new VirtualKeyboard(permissions.Object, () => Mock.Of<IEventSimulator>());
        var disposedDuringRequest = false;
        Thread? disposeThread = null;
        permissions.Setup(x => x.CheckAndRequestAccessibilityPermission()).Returns(() =>
        {
            // A different thread must be able to acquire the keyboard lock before this call returns.
            disposeThread = new Thread(keyboard.Dispose) { IsBackground = true };
            disposeThread.Start();
            disposedDuringRequest = disposeThread.Join(TimeSpan.FromSeconds(5));
            return false;
        });

        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(keyboard.Paste);
            Assert.IsTrue(disposedDuringRequest, "Permission request started while holding the keyboard lock.");
        }
        finally
        {
            disposeThread?.Join(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public void LinuxPermissionNotice_UsesUiDispatcher_AndDoesNotRequestAuthorization()
    {
        var permissions = new Mock<IInputPermissionProvider>();
        permissions.Setup(x => x.GetSimulationStatus()).Returns(Denied);
        var dispatcher = new Mock<IThreadDispatcher>();
        dispatcher.Setup(x => x.RunOnMainThreadAsync(It.IsAny<Func<Task>>()))
            .Returns<Func<Task>>(action => action());
        var dialog = new Mock<IGlobalDialog>();
        dialog.Setup(x => x.ShowMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        using var keyboard = new VirtualKeyboard(permissions.Object, dispatcher.Object, dialog.Object);

        Assert.ThrowsExactly<InvalidOperationException>(keyboard.Paste);
        Assert.ThrowsExactly<InvalidOperationException>(keyboard.Copy);

        dispatcher.Verify(x => x.RunOnMainThreadAsync(It.IsAny<Func<Task>>()), Times.Once);
        dialog.Verify(x => x.ShowMessageAsync(Strings.PermissionManagement,
            Strings.LinuxInputSimulationPermissionRequired, Strings.Confirm), Times.Once);
        permissions.Verify(x => x.CheckAndRequestAccessibilityPermission(), Times.Never);
        permissions.Verify(x => x.GetStatus(), Times.Never);
        Assert.Contains("/dev/uinput", Strings.LinuxInputSimulationPermissionRequired);
        Assert.DoesNotContain("/dev/input", Strings.LinuxInputSimulationPermissionRequired);
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
    public void Shortcut_WithMultipleModifiers_ReleasesInReverseOrder_AndEmptyListDoesNothing()
    {
        var simulator = new Mock<IEventSimulator>();
        var events = new List<(KeyCode Key, bool Pressed)>();
        simulator.Setup(x => x.SimulateKeyPress(It.IsAny<KeyCode>()))
            .Callback<KeyCode>(key => events.Add((key, true))).Returns(UioHookResult.Success);
        simulator.Setup(x => x.SimulateKeyRelease(It.IsAny<KeyCode>()))
            .Callback<KeyCode>(key => events.Add((key, false))).Returns(UioHookResult.Success);
        var permissions = CreatePermissions();
        var created = 0;
        using var keyboard = new VirtualKeyboard(permissions.Object, () =>
        {
            created++;
            return simulator.Object;
        });

        keyboard.SendShortcut();
        Assert.AreEqual(0, created);
        permissions.Verify(x => x.GetSimulationStatus(), Times.Never);

        keyboard.SendShortcut(KeyCode.VcLeftControl, KeyCode.VcLeftShift, KeyCode.VcV);
        (KeyCode, bool)[] expected = [
            (KeyCode.VcLeftControl, true), (KeyCode.VcLeftShift, true), (KeyCode.VcV, true),
            (KeyCode.VcV, false), (KeyCode.VcLeftShift, false), (KeyCode.VcLeftControl, false)
        ];
        CollectionAssert.AreEqual(expected, events.ToArray());
        Assert.AreEqual(1, created);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Shortcut_MidPressFailure_ReleasesAttemptedKeysEvenIfReleaseFails(bool throws)
    {
        var simulator = new Mock<IEventSimulator>();
        var failure = new InvalidOperationException("Press failed");
        simulator.Setup(x => x.SimulateKeyPress(KeyCode.VcLeftShift)).Returns(() =>
            throws ? throw failure : UioHookResult.ErrorAxApiDisabled);
        simulator.Setup(x => x.SimulateKeyRelease(KeyCode.VcLeftShift))
            .Throws(new InvalidOperationException("Release failed"));
        using var keyboard = new VirtualKeyboard(CreatePermissions().Object, () => simulator.Object);

        void Send() => keyboard.SendShortcut(KeyCode.VcLeftControl, KeyCode.VcLeftShift, KeyCode.VcV);
        if (throws)
        {
            Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(Send));
        }
        else
        {
            Assert.AreEqual(UioHookResult.ErrorAxApiDisabled, Assert.ThrowsExactly<HookException>(Send).Result);
        }

        simulator.Verify(x => x.SimulateKeyRelease(KeyCode.VcLeftShift), Times.Once);
        simulator.Verify(x => x.SimulateKeyRelease(KeyCode.VcLeftControl), Times.Once);
        simulator.Verify(x => x.SimulateKeyPress(KeyCode.VcV), Times.Never);
        simulator.Verify(x => x.SimulateKeyRelease(KeyCode.VcV), Times.Never);
        simulator.Verify(x => x.Dispose(), Times.Once);
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
        permissions.SetupSequence(x => x.GetSimulationStatus()).Returns(Allowed).Returns(Denied);
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
        permissions.Setup(x => x.GetSimulationStatus()).Returns(Allowed);
        return permissions;
    }
}
