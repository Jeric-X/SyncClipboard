using Microsoft.Extensions.DependencyInjection;
using Moq;
using NativeNotification.Interface;
using SharpHook.Data;
using SharpHook.Providers;
using SharpHook.Simulation;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test;

[TestClass]
public class InputPermissionTests
{
    internal static InputPermissionProvider CreatePermissionProvider(ILogger logger, Action requestAccessibilityPermission,
        bool isMacOS = true, Func<bool>? isAccessibilityEnabled = null,
        Func<Task<bool>>? confirm = null, Action? openSettings = null)
    {
        var dispatcher = new Mock<IThreadDispatcher>();
        dispatcher.Setup(x => x.RunOnMainThreadAsync(It.IsAny<Func<Task>>()))
            .Returns<Func<Task>>(action => action());
        var dialog = new Mock<IGlobalDialog>();
        dialog.Setup(x => x.ShowConfirmationAsync(Strings.AccessibilityPermission,
            Strings.AccessibilityPermissionRequestMessage, Strings.RequestPermission, Strings.Cancel))
            .Returns(() => confirm?.Invoke() ?? Task.FromResult(true));
        return new InputPermissionProvider(logger, dispatcher.Object, dialog.Object,
            openSettings ?? (() => { }), requestAccessibilityPermission, isMacOS, isAccessibilityEnabled);
    }

    [TestMethod]
    public void AccessibilityConfirmation_CoalescesRequests_AndCancellationAllowsRetry()
    {
        var confirmation = new TaskCompletionSource<bool>();
        var events = new List<string>();
        InputPermissionProvider? provider = null;
        provider = CreatePermissionProvider(Mock.Of<ILogger>(), () =>
        {
            Assert.IsFalse(provider!.HasRequestedAccessibilityPermission);
            events.Add("request");
        }, isAccessibilityEnabled: () => false, confirm: () =>
        {
            events.Add("dialog");
            return confirmation.Task;
        }, openSettings: () =>
        {
            Assert.IsFalse(provider!.HasRequestedAccessibilityPermission);
            events.Add("settings");
        });
        provider.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(provider.HasRequestedAccessibilityPermission)) events.Add("requested");
        };

        Parallel.For(0, 10, _ => Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission()));
        Assert.HasCount(1, events);
        Assert.IsFalse(provider.HasRequestedAccessibilityPermission);
        confirmation.SetResult(false);
        Assert.IsFalse(provider.HasRequestedAccessibilityPermission);
        Assert.HasCount(1, events);

        confirmation = new TaskCompletionSource<bool>();
        Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission());
        Assert.IsFalse(provider.HasRequestedAccessibilityPermission);
        confirmation.SetResult(true);
        Assert.IsTrue(provider.HasRequestedAccessibilityPermission);
        Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission());
        string[] expected = ["dialog", "dialog", "settings", "request", "requested"];
        CollectionAssert.AreEqual(expected, events);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AccessibilityRequest_NotifiesOnce_AndDoesNotRepeatAfterFailure(bool requestFails)
    {
        var requests = 0;
        var logger = new Mock<ILogger>();
        var provider = InputPermissionTests.CreatePermissionProvider(logger.Object, () =>
        {
            requests++;
            if (requestFails) throw new InvalidOperationException("Request failed");
        }, isAccessibilityEnabled: () => false);
        var requestStateChanges = 0;
        provider.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(provider.HasRequestedAccessibilityPermission)) requestStateChanges++;
        };

        Assert.IsFalse(provider.HasRequestedAccessibilityPermission);
        Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission());
        Assert.IsTrue(provider.HasRequestedAccessibilityPermission);
        Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission());
        Assert.AreEqual(1, requests);
        Assert.AreEqual(1, requestStateChanges);
        logger.Verify(x => x.Write(nameof(InputPermissionProvider), It.IsAny<string>()),
            requestFails ? Times.Once() : Times.Never());
    }

    [TestMethod]
    public void AccessibilityCheck_RequestsOnceAcrossCallers_AndRechecksCurrentAccess()
    {
        var granted = true;
        var requests = 0;
        var provider = InputPermissionTests.CreatePermissionProvider(Mock.Of<ILogger>(),
            () => Interlocked.Increment(ref requests), isAccessibilityEnabled: () => granted);

        Assert.IsTrue(provider.CheckAndRequestAccessibilityPermission());
        Assert.AreEqual(0, requests);
        granted = false;
        Assert.AreEqual(InputPermissionState.Denied, provider.GetStatus().Accessibility);
        Assert.AreEqual(0, requests);
        Parallel.For(0, 10, _ => Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission()));
        Assert.AreEqual(1, requests);

        granted = true;
        Assert.IsTrue(provider.CheckAndRequestAccessibilityPermission());
        granted = false;
        Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission());
        Assert.AreEqual(1, requests);
    }

    [TestMethod]
    public void AccessibilityRequest_OnOtherPlatforms_DoesNotRequest()
    {
        var requests = 0;
        var provider = InputPermissionTests.CreatePermissionProvider(Mock.Of<ILogger>(), () => requests++, isMacOS: false);

        Assert.IsTrue(provider.CheckAndRequestAccessibilityPermission());
        Assert.IsFalse(provider.HasRequestedAccessibilityPermission);
        Assert.AreEqual(0, requests);
    }

    [TestMethod]
    [DataRow(LinuxMode.AutoXRecord, false, LinuxBackend.XRecord)]
    [DataRow(LinuxMode.AutoXRecord, true, LinuxBackend.Wayland)]
    [DataRow(LinuxMode.AutoLowLevel, false, LinuxBackend.X11)]
    [DataRow(LinuxMode.AutoLowLevel, true, LinuxBackend.Wayland)]
    [DataRow(LinuxMode.XRecord, false, LinuxBackend.XRecord)]
    [DataRow(LinuxMode.XRecord, true, LinuxBackend.XRecord)]
    public void FirstLinuxPaste_CanInitializeBackend_WithoutRegisteredHotkeys(
        LinuxMode mode, bool isWayland, LinuxBackend expectedBackend)
    {
        var backend = LinuxBackend.None;
        var provider = new Mock<ILinuxBackendProvider>();
        provider.Setup(x => x.GetLoadedLinuxBackend()).Returns(() => backend);
        provider.Setup(x => x.GetLinuxMode()).Returns(mode);
        var deviceChecks = 0;
        var permissions = new Mock<IInputPermissionProvider>();
        permissions.Setup(x => x.GetSimulationStatus()).Returns(() => InputPermissionProvider.GetLinuxStatus(
            provider.Object, isWayland, () =>
            {
                deviceChecks++;
                return new(InputPermissionState.NotRequired, InputPermissionState.Available, InputPermissionState.Available);
            }));
        var simulator = new Mock<IEventSimulator>();
        var created = 0;
        using var keyboard = new VirtualKeyboard(permissions.Object, () =>
        {
            created++;
            backend = expectedBackend;
            return simulator.Object;
        });

        keyboard.Paste();
        keyboard.Paste();

        Assert.AreEqual(1, created);
        Assert.AreEqual(expectedBackend == LinuxBackend.XRecord ? 0 : 2, deviceChecks);
        simulator.Verify(x => x.SimulateKeyPress(KeyCode.VcV), Times.Exactly(2));
        // Once loaded, the actual backend takes precedence over mode/session inference.
        provider.Verify(x => x.GetLinuxMode(), Times.Once);
    }

    [TestMethod]
    [DataRow(LinuxMode.AutoXRecord, true)]
    [DataRow(LinuxMode.AutoLowLevel, false)]
    [DataRow(LinuxMode.AutoLowLevel, true)]
    public void UnloadedLowLevelBackend_StillRequiresDevicePermission(LinuxMode mode, bool isWayland)
    {
        var provider = new Mock<ILinuxBackendProvider>();
        provider.Setup(x => x.GetLoadedLinuxBackend()).Returns(LinuxBackend.None);
        provider.Setup(x => x.GetLinuxMode()).Returns(mode);
        var denied = new InputPermissionStatus(
            InputPermissionState.NotRequired, InputPermissionState.Denied, InputPermissionState.Denied);
        var status = InputPermissionProvider.GetLinuxStatus(provider.Object, isWayland, () => denied);
        Assert.AreEqual(denied, status);
        Assert.IsFalse(status.CanSimulateInput);
    }

    [TestMethod]
    [DataRow(ClipboardOwnerFilterConfig.ConfigKey)]
    [DataRow(ClipboardOwnerFilterConfig.EasyCopyImageFilterConfigKey)]
    public void ClipboardFilter_RequestsOnlyWhenUserEnables(string configKey)
    {
        using var migrationServices = new ConfigurationTestServices();
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var config = new ConfigManager(Path.Combine(directory.FullName, "config.json"), migrationServices.Upgrader);
            config.SetConfig(configKey, new ClipboardOwnerFilterConfig { FilterMode = "BlackList" });
            var permissions = new Mock<IInputPermissionProvider>();
            var viewModel = new ClipboardOwnerFilterSettingViewModel(config, Mock.Of<IClipboardChangingListener>(), permissions.Object);
            viewModel.UseConfig(configKey);
            config.SetConfig(configKey, new ClipboardOwnerFilterConfig { FilterMode = "WhiteList" });
            permissions.Verify(x => x.CheckAndRequestAccessibilityPermission(), Times.Never);

            viewModel.FilterMode = ClipboardOwnerFilterSettingViewModel.Modes[0];
            permissions.Verify(x => x.CheckAndRequestAccessibilityPermission(), Times.Never);
            viewModel.FilterMode = ClipboardOwnerFilterSettingViewModel.Modes[1];
            permissions.Verify(x => x.CheckAndRequestAccessibilityPermission(), Times.Once);
            viewModel.FilterMode = ClipboardOwnerFilterSettingViewModel.Modes[2];
            permissions.Verify(x => x.CheckAndRequestAccessibilityPermission(), Times.Once);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void HotkeyFilter_RequestsOnlyWhenUserEnables()
    {
        using var migrationServices = new ConfigurationTestServices();
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var config = new ConfigManager(Path.Combine(directory.FullName, "config.json"), migrationServices.Upgrader);
            config.SetConfig(new HotkeyBlacklistConfig { Enabled = true });
            var registry = Mock.Of<INativeHotkeyRegistry>();
            var permissions = new Mock<IInputPermissionProvider>();
            var viewModel = new HotkeyBlacklistViewModel(config,
                new ForegroundWindowCapture(registry, Mock.Of<INativeWindowController>()),
                new HotkeyManager(registry, config), permissions.Object);
            config.SetConfig(new HotkeyBlacklistConfig { Enabled = false });
            config.SetConfig(new HotkeyBlacklistConfig { Enabled = true });
            permissions.Verify(x => x.CheckAndRequestAccessibilityPermission(), Times.Never);

            viewModel.IsEnabled = false;
            permissions.Verify(x => x.CheckAndRequestAccessibilityPermission(), Times.Never);
            viewModel.IsEnabled = true;
            viewModel.IsEnabled = true;
            permissions.Verify(x => x.CheckAndRequestAccessibilityPermission(), Times.Once);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void PermissionStatus_RefreshesWithoutRequesting_AndSettingsRemainAccessible()
    {
        using var migrationServices = new ConfigurationTestServices();
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var permissions = new Mock<IInputPermissionProvider>(MockBehavior.Strict);
            var state = InputPermissionState.Denied;
            permissions.Setup(x => x.GetStatus()).Returns(() => new InputPermissionStatus(state, state, state));
            using var services = new ServiceCollection()
                .AddSingleton(permissions.Object)
                .AddSingleton(Mock.Of<ILogger>())
                .BuildServiceProvider();
            var config = new ConfigManager(Path.Combine(directory.FullName, "config.json"), migrationServices.Upgrader);
            var viewModel = new SystemSettingViewModel(config, new StaticConfig(Mock.Of<INotificationManager>()), services);

            foreach (var permission in new[] { InputPermissionState.Denied, InputPermissionState.Available, InputPermissionState.Denied })
            {
                state = permission;
                viewModel.RefreshInputPermissions();
                Assert.AreEqual(state, viewModel.InputPermissions.Accessibility);
                Assert.AreEqual(OperatingSystem.IsMacOS(), viewModel.OpenAccessibilitySettingsCommand.CanExecute(null));
            }
            permissions.Verify(x => x.GetStatus(), Times.Exactly(3));
            permissions.VerifyNoOtherCalls();
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void AllMacPermissions_UseCurrentAccessibilityState_WithoutRequesting()
    {
        var trusted = false;
        var provider = CreatePermissionProvider(Mock.Of<ILogger>(),
            () => Assert.Fail("Status checks must not request permission."),
            isAccessibilityEnabled: () => trusted);

        foreach (var granted in new[] { false, true, false })
        {
            trusted = granted;
            var state = granted ? InputPermissionState.Available : InputPermissionState.Denied;
            var expected = new InputPermissionStatus(state, state, state);
            Assert.AreEqual(expected, provider.GetStatus());
            Assert.AreEqual(expected, provider.GetSimulationStatus());
            Assert.AreEqual(granted, provider.GetSimulationStatus().CanSimulateInput);
            if (granted) Assert.IsTrue(provider.CheckAndRequestAccessibilityPermission());
            Assert.IsFalse(provider.HasRequestedAccessibilityPermission);
        }
    }

    [TestMethod]
    public void FailedAccessibilityCheck_MakesAllMacPermissionsUnknown()
    {
        var provider = CreatePermissionProvider(Mock.Of<ILogger>(), () => { },
            isAccessibilityEnabled: () => throw new DllNotFoundException());
        var expected = new InputPermissionStatus(
            InputPermissionState.Unknown, InputPermissionState.Unknown, InputPermissionState.Unknown);
        Assert.AreEqual(expected, provider.GetStatus());
        Assert.AreEqual(expected, provider.GetSimulationStatus());
        Assert.IsFalse(provider.GetSimulationStatus().CanSimulateInput);
    }

    [TestMethod]
    public void MissingDevices_AreUnavailable_AndAreNotCreated()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var status = InputPermissionProvider.GetLinuxDeviceStatus(directory, Path.Combine(directory, "uinput"));
        Assert.AreEqual(InputPermissionState.Unavailable, status.KeyboardMonitoring);
        Assert.AreEqual(InputPermissionState.Unavailable, status.InputSimulation);
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void DeviceAccessCheck_DoesNotReadOrWriteInput_AndRechecksMissingDevices()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var input = Path.Combine(directory.FullName, "event0");
            var output = Path.Combine(directory.FullName, "uinput");
            File.WriteAllText(input, "input unchanged");
            File.WriteAllText(output, "output unchanged");
            var status = InputPermissionProvider.GetLinuxDeviceStatus(directory.FullName, output);
            Assert.AreEqual(InputPermissionState.Available, status.KeyboardMonitoring);
            Assert.AreEqual(InputPermissionState.Available, status.InputSimulation);
            Assert.AreEqual("input unchanged", File.ReadAllText(input));
            Assert.AreEqual("output unchanged", File.ReadAllText(output));

            File.Delete(output);
            status = InputPermissionProvider.GetLinuxDeviceStatus(directory.FullName, output);
            Assert.AreEqual(InputPermissionState.Available, status.KeyboardMonitoring);
            Assert.AreEqual(InputPermissionState.Unavailable, status.InputSimulation);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void ReadOnlyInputDevice_IsDeniedUntilWriteAccessIsGranted()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Inconclusive("Requires Unix file permissions without root privileges.");
            return;
        }

        var directory = Directory.CreateTempSubdirectory();
        var input = Path.Combine(directory.FullName, "event0");
        try
        {
            var output = Path.Combine(directory.FullName, "uinput");
            File.WriteAllText(input, "input unchanged");
            File.WriteAllText(output, "output unchanged");
            File.SetUnixFileMode(input, UnixFileMode.UserRead);
            var denied = InputPermissionProvider.GetLinuxDeviceStatus(directory.FullName, output);
            Assert.AreEqual(InputPermissionState.Denied, denied.KeyboardMonitoring);
            Assert.AreEqual(InputPermissionState.Available, denied.InputSimulation);

            File.SetUnixFileMode(input, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var allowed = InputPermissionProvider.GetLinuxDeviceStatus(directory.FullName, output);
            Assert.AreEqual(InputPermissionState.Available, allowed.KeyboardMonitoring);
            Assert.AreEqual("input unchanged", File.ReadAllText(input));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void SimulationPermission_RequiresOnlyWriteAccessToUinput()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Inconclusive("Requires Unix file permissions without root privileges.");
            return;
        }

        var directory = Directory.CreateTempSubdirectory();
        var output = Path.Combine(directory.FullName, "uinput");
        try
        {
            File.WriteAllText(output, "output unchanged");
            File.SetUnixFileMode(output, UnixFileMode.UserWrite);
            var status = InputPermissionProvider.GetLinuxSimulationStatus(output);
            Assert.IsTrue(status.CanSimulateInput);
            Assert.AreEqual(InputPermissionState.Unknown, status.KeyboardMonitoring);

            File.SetUnixFileMode(output, UnixFileMode.UserRead);
            status = InputPermissionProvider.GetLinuxSimulationStatus(output);
            Assert.AreEqual(InputPermissionState.Denied, status.InputSimulation);
            Assert.IsFalse(status.CanSimulateInput);
            Assert.AreEqual("output unchanged", File.ReadAllText(output));

            File.Delete(output);
            Assert.AreEqual(InputPermissionState.Unavailable,
                InputPermissionProvider.GetLinuxSimulationStatus(output).InputSimulation);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void PermissionLabels_DistinguishMissingDeniedAndUnknown()
    {
        Assert.AreEqual(Strings.InputPermissionDenied,
            SystemSettingViewModel.GetPermissionText(InputPermissionState.Denied));
        Assert.AreEqual(Strings.InputPermissionUnavailable,
            SystemSettingViewModel.GetPermissionText(InputPermissionState.Unavailable));
        Assert.AreEqual(Strings.InputPermissionUnknown,
            SystemSettingViewModel.GetPermissionText(InputPermissionState.Unknown));
        var labels = Enum.GetValues<InputPermissionState>().Select(SystemSettingViewModel.GetPermissionText).ToArray();
        Assert.AreEqual(labels.Length, labels.Distinct().Count());
    }
}
