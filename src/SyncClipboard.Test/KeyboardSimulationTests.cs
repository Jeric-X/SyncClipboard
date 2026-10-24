using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpHook;
using SharpHook.Data;
using SharpHook.Simulation;
using SyncClipboard.Core;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.Permissions;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.Utilities.Permissions;

namespace SyncClipboard.Test;

[TestClass]
public class KeyboardSimulationTests
{
    private const PermissionState Allowed = PermissionState.Available;
    private const PermissionState Denied = PermissionState.Denied;

    [TestMethod]
    public void AutomaticAccessibilityRequest_SkipsTriggeringInput()
    {
        var events = new List<string>();
        var status = Denied;
        var provider = PermissionTests.CreatePermissionProvider(Mock.Of<ILogger>(), () =>
        {
            events.Add("request");
            status = Allowed;
        }, isAccessibilityEnabled: () => false);
        var permissions = new Mock<IPermissionProvider>();
        permissions.Setup(x => x.GetState(PermissionCapability.SimulateInput)).Returns(() => status);
        permissions.Setup(x => x.CheckAndRequest(PermissionCapability.SimulateInput)).Returns(provider.CheckAndRequest);
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

        string[] expected = ["request", "create simulator"];
        CollectionAssert.AreEqual(expected, events);
        permissions.Verify(x => x.CheckAndRequest(PermissionCapability.SimulateInput), Times.Once);
        simulator.Verify(x => x.SimulateKeyPress(KeyCode.VcV), Times.Once);
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(false, false)]
    public void MissingMacPermission_ResetsAfterConfirmation_AndSkipsInputUntilBothGranted(bool ax, bool cg)
    {
        var confirmation = new TaskCompletionSource<bool>();
        var events = new List<string>();
        var provider = PermissionTests.CreatePermissionProvider(Mock.Of<ILogger>(),
            () => events.Add("request"), isAccessibilityEnabled: () => ax, canPostEvents: () => cg,
            confirm: () => confirmation.Task, openSettings: () => events.Add("settings"), resetPermission: () =>
            {
                events.Add("reset");
                return Task.CompletedTask;
            });
        var simulator = new Mock<IEventSimulator>();
        using var keyboard = new VirtualKeyboard(provider, () =>
        {
            events.Add("simulator");
            return simulator.Object;
        });

        Assert.ThrowsExactly<InvalidOperationException>(keyboard.Paste);
        Assert.IsEmpty(events, "Permission reset and input must wait for confirmation.");
        confirmation.SetResult(true);
        string[] requestedEvents = ["reset", "settings", "request"];
        CollectionAssert.AreEqual(requestedEvents, events);
        simulator.Verify(x => x.SimulateKeyPress(It.IsAny<KeyCode>()), Times.Never);

        ax = true;
        cg = true;
        keyboard.Paste();
        string[] completedEvents = ["reset", "settings", "request", "simulator"];
        CollectionAssert.AreEqual(completedEvents, events);
        simulator.Verify(x => x.SimulateKeyPress(KeyCode.VcV), Times.Once);
        simulator.Verify(x => x.SimulateKeyRelease(KeyCode.VcV), Times.Once);
    }

    [TestMethod]
    public void ServiceContainer_ExposesOnlyWrapper_AndDoesNotCheckPermissionsDuringResolution()
    {
        var services = new ServiceCollection();
        AppCore.ConfigCommonService(services);
        var permissions = new Mock<IPermissionProvider>(MockBehavior.Strict);
        services.AddSingleton(permissions.Object);
        services.AddSingleton(Mock.Of<IThreadDispatcher>());
        services.AddSingleton(Mock.Of<IGlobalDialog>());
        services.AddSingleton(Mock.Of<ILogger>());
        using var provider = services.BuildServiceProvider();

        var keyboard = provider.GetRequiredService<VirtualKeyboard>();

        Assert.AreSame(keyboard, provider.GetRequiredService<VirtualKeyboard>());
        Assert.IsNull(provider.GetService<IEventSimulator>());
        Assert.IsNull(provider.GetService<Lazy<IEventSimulator>>());
        permissions.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow(PermissionState.Denied, PermissionState.Available)]
    [DataRow(PermissionState.Denied, PermissionState.NotRequired)]
    [DataRow(PermissionState.Unknown, PermissionState.Available)]
    [DataRow(PermissionState.Unavailable, PermissionState.Available)]
    [DataRow(PermissionState.Partial, PermissionState.Available)]
    public void MissingPermission_DoesNotCreateSimulator_AndCanRecoverAfterGrant(
        PermissionState missing, PermissionState granted)
    {
        var permissions = new Mock<IPermissionProvider>();
        permissions.SetupSequence(x => x.GetState(PermissionCapability.SimulateInput)).Returns(missing).Returns(granted);
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
    [DataRow("cancel")]
    [DataRow("request-failure")]
    [DataRow("still-denied")]
    public async Task Simulation_AutomaticRequestIsLimited_ButSettingsCanRetry(string outcome)
    {
        var granted = true;
        var dialogs = 0;
        var requests = 0;
        var provider = PermissionTests.CreatePermissionProvider(Mock.Of<ILogger>(), () =>
        {
            requests++;
            if (outcome == "request-failure" && requests == 1)
                throw new InvalidOperationException("Native request failed.");
        }, isAccessibilityEnabled: () => granted, confirm: () =>
        {
            dialogs++;
            return Task.FromResult(outcome != "cancel" || dialogs > 1);
        });
        var simulator = new Mock<IEventSimulator>();
        using var keyboard = new VirtualKeyboard(provider, () => simulator.Object);

        keyboard.Paste();
        Assert.AreEqual(0, dialogs, "Authorized input must not consume the automatic request opportunity.");
        granted = false;
        Parallel.For(0, 10, _ => Assert.ThrowsExactly<InvalidOperationException>(keyboard.Paste));
        Assert.AreEqual(1, dialogs);
        Assert.AreEqual(outcome == "cancel" ? 0 : 1, requests);
        simulator.Verify(x => x.SimulateKeyPress(KeyCode.VcV), Times.Once);

        for (var attempt = 0; attempt < 2; attempt++)
            await provider.GetPermissions().Single().Actions.Single().ExecuteAsync();
        Assert.AreEqual(3, dialogs, "Settings actions remain available after automatic request cancellation or failure.");
        Assert.AreEqual(outcome == "cancel" ? 2 : 3, requests);

        granted = true;
        keyboard.Paste();
        simulator.Verify(x => x.SimulateKeyPress(KeyCode.VcV), Times.Exactly(2));
        granted = false;
        Assert.ThrowsExactly<InvalidOperationException>(keyboard.Copy);
        Assert.AreEqual(3, dialogs, "Granting and revoking access must not replenish the automatic request opportunity.");
    }

    [TestMethod]
    public void AccessibilityRequest_StartsOutsideKeyboardLock()
    {
        var permissions = new Mock<IPermissionProvider>();
        permissions.Setup(x => x.GetState(PermissionCapability.SimulateInput)).Returns(Denied);
        using var keyboard = new VirtualKeyboard(permissions.Object, () => Mock.Of<IEventSimulator>());
        var disposedDuringRequest = false;
        Thread? disposeThread = null;
        permissions.Setup(x => x.CheckAndRequest(PermissionCapability.SimulateInput)).Returns(() =>
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
    public void LinuxPermissionNotice_UsesUiDispatcher_AndOnlyAppearsOnFirstDeniedInput()
    {
        var backend = new Mock<SharpHook.Providers.ILinuxBackendProvider>();
        backend.Setup(x => x.GetLoadedLinuxBackend()).Returns(LinuxBackend.Wayland);
        var dispatcher = new Mock<IThreadDispatcher>();
        dispatcher.Setup(x => x.RunOnMainThreadAsync(It.IsAny<Func<Task>>()))
            .Returns<Func<Task>>(action => action());
        var dialog = new Mock<IGlobalDialog>();
        dialog.Setup(x => x.ShowMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var permissions = new LinuxPermissionProvider(backend.Object, () => true,
            () => throw new AssertFailedException("Simulation must not check keyboard monitoring."),
            () => PermissionState.Denied, dispatcher.Object, dialog.Object);
        using var keyboard = new VirtualKeyboard(permissions, () => throw new AssertFailedException("Input must be skipped."));

        Assert.ThrowsExactly<InvalidOperationException>(keyboard.Paste);
        Assert.ThrowsExactly<InvalidOperationException>(keyboard.Copy);

        dispatcher.Verify(x => x.RunOnMainThreadAsync(It.IsAny<Func<Task>>()), Times.Once);
        dialog.Verify(x => x.ShowMessageAsync(Strings.PermissionManagement,
            Strings.LinuxInputSimulationPermissionRequired, Strings.Confirm), Times.Once);
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
            if (++created == 1)
                throw new HookException(UioHookResult.ErrorLinuxOpenUinput);
            return simulator.Object;
        });

        Assert.ThrowsExactly<HookException>(keyboard.Paste);
        keyboard.Paste();
        Assert.AreEqual(2, created);
    }

    [TestMethod]
    [TestCategory("PlatformWindows")]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
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
        permissions.Verify(x => x.GetState(PermissionCapability.SimulateInput), Times.Never);

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
    [TestCategory("PlatformWindows")]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
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
        var permissions = new Mock<IPermissionProvider>();
        permissions.SetupSequence(x => x.GetState(PermissionCapability.SimulateInput)).Returns(Allowed).Returns(Denied);
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

    private static Mock<IPermissionProvider> CreatePermissions()
    {
        var permissions = new Mock<IPermissionProvider>();
        permissions.Setup(x => x.GetState(PermissionCapability.SimulateInput)).Returns(Allowed);
        return permissions;
    }
}
