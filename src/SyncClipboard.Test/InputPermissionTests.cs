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
    public TestContext TestContext { get; set; } = null!;

    internal static MacOSInputPermissionProvider CreatePermissionProvider(ILogger logger, Action requestAccessibilityPermission,
        Func<bool>? isAccessibilityEnabled = null,
        Func<Task<bool>>? confirm = null, Action? openSettings = null, Func<Task>? resetPermission = null,
        Func<bool>? canPostEvents = null)
    {
        var dispatcher = new Mock<IThreadDispatcher>();
        dispatcher.Setup(x => x.RunOnMainThreadAsync(It.IsAny<Func<Task>>()))
            .Returns<Func<Task>>(action => action());
        var dialog = new Mock<IGlobalDialog>();
        dialog.Setup(x => x.ShowConfirmationAsync(Strings.AccessibilityPermission,
            Strings.AccessibilityPermissionRequestMessage, Strings.RequestPermission, Strings.Cancel))
            .Returns(() => confirm?.Invoke() ?? Task.FromResult(true));
        return new MacOSInputPermissionProvider(logger, dispatcher.Object, dialog.Object,
            openSettings ?? (() => { }), requestAccessibilityPermission, resetPermission ?? (() => Task.CompletedTask),
            isAccessibilityEnabled ?? (() => false), canPostEvents ?? (() => true));
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void AccessibilityConfirmation_CoalescesRequests_AndCancellationAllowsRetry(bool ax, bool cg)
    {
        var confirmation = new TaskCompletionSource<bool>();
        var events = new List<string>();
        var provider = CreatePermissionProvider(Mock.Of<ILogger>(), () => events.Add("request"),
            isAccessibilityEnabled: () => ax, canPostEvents: () => cg, confirm: () =>
        {
            events.Add("dialog");
            return confirmation.Task;
        }, openSettings: () => events.Add("settings"), resetPermission: () =>
        {
            events.Add("reset");
            return Task.CompletedTask;
        });

        Parallel.For(0, 10, _ => Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission()));
        Assert.HasCount(1, events);
        confirmation.SetResult(false);
        Assert.HasCount(1, events);

        confirmation = new TaskCompletionSource<bool>();
        Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission());
        confirmation.SetResult(true);
        string[] expected = ["dialog", "dialog", "reset", "settings", "request"];
        CollectionAssert.AreEqual(expected, events);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AccessibilityGrantedDuringConfirmation_IsPreserved_AndLaterRevocationAllowsRequest(bool axAlreadyGranted)
    {
        var confirmation = new TaskCompletionSource<bool>();
        var granted = false;
        var resets = 0;
        var requests = 0;
        var settings = 0;
        var provider = CreatePermissionProvider(Mock.Of<ILogger>(), () => requests++,
            isAccessibilityEnabled: () => axAlreadyGranted || granted, canPostEvents: () => granted,
            confirm: () => confirmation.Task,
            openSettings: () => settings++, resetPermission: () =>
            {
                resets++;
                return Task.CompletedTask;
            });

        Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission());
        granted = true;
        confirmation.SetResult(true);

        Assert.AreEqual(0, resets);
        Assert.AreEqual(0, requests);
        Assert.AreEqual(0, settings);
        Assert.IsTrue(provider.CheckAndRequestAccessibilityPermission());

        granted = false;
        Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission());
        Assert.AreEqual(1, resets);
        Assert.AreEqual(1, requests);
        Assert.AreEqual(1, settings);
    }

    [TestMethod]
    public async Task AccessibilityRequest_WaitsForReset_AndCoalescesRequestsWhileResetting()
    {
        var reset = new TaskCompletionSource();
        var requested = new TaskCompletionSource();
        var events = new List<string>();
        var provider = CreatePermissionProvider(Mock.Of<ILogger>(), () =>
        {
            events.Add("request");
            requested.SetResult();
        }, isAccessibilityEnabled: () => false, openSettings: () => events.Add("settings"), resetPermission: () =>
        {
            events.Add("reset");
            return reset.Task;
        });

        Parallel.For(0, 10, _ => Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission()));
        string[] resettingEvents = ["reset"];
        CollectionAssert.AreEqual(resettingEvents, events);

        reset.SetResult();
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        string[] requestedEvents = ["reset", "settings", "request"];
        CollectionAssert.AreEqual(requestedEvents, events);
    }

    [TestMethod]
    [DataRow("failure")]
    [DataRow("timeout")]
    [DataRow("start")]
    public void AccessibilityResetFailure_IsLogged_AndContinuesNativeRequest(string failure)
    {
        var events = new List<string>();
        var logger = new Mock<ILogger>();
        var provider = CreatePermissionProvider(logger.Object, () => events.Add("request"),
            isAccessibilityEnabled: () => false, confirm: () =>
            {
                events.Add("dialog");
                return Task.FromResult(true);
            }, openSettings: () => events.Add("settings"), resetPermission: () =>
            {
                events.Add("reset");
                if (failure == "start")
                    throw new InvalidOperationException("Reset failed to start");
                return Task.FromException(failure == "timeout"
                    ? new TimeoutException("Reset timed out") : new InvalidOperationException("Reset failed"));
            });

        Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission());
        string[] expected = ["dialog", "reset", "settings", "request"];
        CollectionAssert.AreEqual(expected, events);
        logger.Verify(x => x.Write(nameof(MacOSInputPermissionProvider),
            It.Is<string>(message => message.StartsWith("Failed to reset accessibility permission:"))), Times.Once);
    }

    [TestMethod]
    [DataRow("none")]
    [DataRow("settings")]
    [DataRow("request")]
    public void AccessibilityRequest_LogsSettingsAndNativeRequestFailures(string failure)
    {
        var requests = 0;
        var logger = new Mock<ILogger>();
        var provider = InputPermissionTests.CreatePermissionProvider(logger.Object, () =>
        {
            requests++;
            if (failure == "request")
                throw new InvalidOperationException("Request failed");
        }, isAccessibilityEnabled: () => false, openSettings: () =>
        {
            if (failure == "settings")
                throw new InvalidOperationException("Settings failed");
        });

        Assert.IsFalse(provider.CheckAndRequestAccessibilityPermission());
        Assert.AreEqual(failure == "settings" ? 0 : 1, requests);
        logger.Verify(x => x.Write(nameof(MacOSInputPermissionProvider), It.IsAny<string>()),
            failure == "none" ? Times.Never() : Times.Once());
    }

    [TestMethod]
    public void WindowsPermissions_RequireNoAuthorizationOrSettingsEntries()
    {
        var provider = new WindowsInputPermissionProvider();
        Assert.IsTrue(provider.CheckAndRequestAccessibilityPermission());
        Assert.IsTrue(provider.GetSimulationStatus().CanSimulateInput);
        Assert.AreEqual(InputPermissionState.NotRequired, provider.GetStatus().Accessibility);
        Assert.IsEmpty(provider.GetPermissions());
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
        permissions.Setup(x => x.GetSimulationStatus()).Returns(() => LinuxInputPermissionProvider.GetLinuxStatus(
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
        var status = LinuxInputPermissionProvider.GetLinuxStatus(provider.Object, isWayland, () => denied);
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
    public async Task SettingsPermissionList_UsesProviderEntries_AndRefreshesAfterActions()
    {
        using var migrationServices = new ConfigurationTestServices();
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var permissions = new Mock<IInputPermissionProvider>(MockBehavior.Strict);
            var state = InputPermissionState.Denied;
            var operations = 0;
            IReadOnlyList<InputPermissionItem> GetPermissions() =>
            [new("Permission", "Description", state, [new("Authorize", () =>
            {
                operations++;
                state = InputPermissionState.Available;
                return Task.CompletedTask;
            })])];
            permissions.Setup(x => x.GetPermissions()).Returns(GetPermissions);
            using var services = new ServiceCollection()
                .AddSingleton(permissions.Object)
                .AddSingleton(Mock.Of<ILogger>())
                .BuildServiceProvider();
            var config = new ConfigManager(Path.Combine(directory.FullName, "config.json"), migrationServices.Upgrader);
            var viewModel = new SystemSettingViewModel(config, new StaticConfig(Mock.Of<INotificationManager>()), services);

            viewModel.RefreshInputPermissions();
            Assert.IsTrue(viewModel.ShowInputPermissions);
            var item = viewModel.InputPermissions.Single();
            Assert.AreEqual("Permission", item.Name);
            Assert.AreEqual("Description", item.Description);
            Assert.AreEqual(Strings.InputPermissionDenied, item.StateText);
            Assert.AreEqual(0, operations, "Refreshing must never request authorization.");

            var action = item.Actions.Single();
            Assert.AreEqual("Authorize", action.Text);
            await action.Command.ExecuteAsync(null);
            Assert.AreEqual(1, operations);
            Assert.AreEqual(Strings.InputPermissionAvailable, viewModel.InputPermissions.Single().StateText);

            permissions.Setup(x => x.GetPermissions()).Returns([]);
            viewModel.RefreshInputPermissions();
            Assert.IsFalse(viewModel.ShowInputPermissions);
            Assert.IsEmpty(viewModel.InputPermissions);
            permissions.Verify(x => x.GetPermissions(), Times.Exactly(3));
            permissions.VerifyNoOtherCalls();
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    [DataRow(LinuxBackend.XRecord, 0)]
    [DataRow(LinuxBackend.Wayland, 2)]
    [DataRow(LinuxBackend.X11, 2)]
    public void LinuxPermissionList_ReflectsBackendDeviceRequirements(LinuxBackend backend, int count)
    {
        var native = new Mock<ILinuxBackendProvider>();
        native.Setup(x => x.GetLoadedLinuxBackend()).Returns(backend);
        var deviceChecks = 0;
        var provider = new LinuxInputPermissionProvider(native.Object, () => false, () =>
        {
            deviceChecks++;
            return new(InputPermissionState.NotRequired, InputPermissionState.Partial, InputPermissionState.Denied);
        }, () => new(InputPermissionState.NotRequired, InputPermissionState.Unknown, InputPermissionState.Available));

        Assert.IsTrue(provider.GetSimulationStatus().CanSimulateInput);
        Assert.AreEqual(0, deviceChecks, "Simulation should not scan keyboard-monitoring devices.");
        var entries = provider.GetPermissions();
        Assert.HasCount(count, entries);
        if (count > 0)
        {
            Assert.AreEqual("/dev/input/event*", entries[0].Name);
            Assert.AreEqual(InputPermissionState.Partial, entries[0].State);
            Assert.AreEqual("/dev/uinput", entries[1].Name);
            Assert.AreEqual(InputPermissionState.Denied, entries[1].State);
            Assert.IsTrue(entries.All(entry => entry.Actions.Count == 0));
        }
        Assert.AreEqual(count > 0 ? 1 : 0, deviceChecks);
    }

    [TestMethod]
    public async Task MacPermissionList_OffersAuthorizationOrSettingsBasedOnCombinedAccess()
    {
        var granted = false;
        var events = new List<string>();
        var provider = CreatePermissionProvider(Mock.Of<ILogger>(), () => events.Add("request"),
            isAccessibilityEnabled: () => true, canPostEvents: () => granted,
            confirm: () =>
            {
                events.Add("confirm");
                return Task.FromResult(true);
            }, openSettings: () => events.Add("settings"), resetPermission: () =>
            {
                events.Add("reset");
                return Task.CompletedTask;
            });

        var entry = provider.GetPermissions().Single();
        Assert.AreEqual(Strings.AccessibilityPermission, entry.Name);
        Assert.AreEqual(InputPermissionState.Denied, entry.State);
        Assert.IsEmpty(events);
        Assert.AreEqual(Strings.RequestPermission, entry.Actions.Single().Text);
        await entry.Actions.Single().ExecuteAsync();
        string[] requestedEvents = ["confirm", "reset", "settings", "request"];
        CollectionAssert.AreEqual(requestedEvents, events);

        granted = true;
        events.Clear();
        entry = provider.GetPermissions().Single();
        Assert.AreEqual(InputPermissionState.Available, entry.State);
        Assert.AreEqual(Strings.OpenSystemSettings, entry.Actions.Single().Text);
        await entry.Actions.Single().ExecuteAsync();
        string[] settingsEvents = ["settings"];
        CollectionAssert.AreEqual(settingsEvents, events);
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
            if (granted)
                Assert.IsTrue(provider.CheckAndRequestAccessibilityPermission());
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void MacAccessibility_RequiresBothChecks_ForAllFeatures(bool ax, bool cg)
    {
        var cgChecks = 0;
        var provider = CreatePermissionProvider(Mock.Of<ILogger>(),
            () => Assert.Fail("Status checks must not request permission."),
            isAccessibilityEnabled: () => ax, canPostEvents: () =>
            {
                cgChecks++;
                return cg;
            });

        var simulationState = ax && cg ? InputPermissionState.Available : InputPermissionState.Denied;
        var expected = new InputPermissionStatus(simulationState, simulationState, simulationState);
        Assert.AreEqual(expected, provider.GetStatus());
        Assert.AreEqual(expected, provider.GetSimulationStatus());
        Assert.AreEqual(ax ? 2 : 0, cgChecks, "AX denial must not initialize the event-posting permission check.");

        ax = true;
        cg = true;
        Assert.IsTrue(provider.GetSimulationStatus().CanSimulateInput);
    }

    [TestMethod]
    public void FailedEventPostingCheck_MakesUnifiedAccessibilityUnknown()
    {
        var provider = CreatePermissionProvider(Mock.Of<ILogger>(), () => { },
            isAccessibilityEnabled: () => true, canPostEvents: () => throw new DllNotFoundException());
        var status = provider.GetStatus();
        Assert.AreEqual(InputPermissionState.Unknown, status.Accessibility);
        Assert.AreEqual(InputPermissionState.Unknown, status.InputSimulation);
        Assert.IsFalse(status.CanSimulateInput);
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
        var status = LinuxInputPermissionProvider.GetLinuxDeviceStatus(directory, Path.Combine(directory, "uinput"));
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
            var status = LinuxInputPermissionProvider.GetLinuxDeviceStatus(directory.FullName, output);
            Assert.AreEqual(InputPermissionState.Available, status.KeyboardMonitoring);
            Assert.AreEqual(InputPermissionState.Available, status.InputSimulation);
            Assert.AreEqual("input unchanged", File.ReadAllText(input));
            Assert.AreEqual("output unchanged", File.ReadAllText(output));

            File.Delete(output);
            status = LinuxInputPermissionProvider.GetLinuxDeviceStatus(directory.FullName, output);
            Assert.AreEqual(InputPermissionState.Available, status.KeyboardMonitoring);
            Assert.AreEqual(InputPermissionState.Unavailable, status.InputSimulation);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    [TestCategory("PlatformLinux")]
    public void ReadOnlyInputDevice_IsDeniedUntilWriteAccessIsGranted()
    {
        if (!OperatingSystem.IsLinux() || Environment.UserName == "root")
        {
            Assert.Inconclusive("Requires Linux file permissions without root privileges.");
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
            var denied = LinuxInputPermissionProvider.GetLinuxDeviceStatus(directory.FullName, output);
            Assert.AreEqual(InputPermissionState.Denied, denied.KeyboardMonitoring);
            Assert.AreEqual(InputPermissionState.Available, denied.InputSimulation);

            File.SetUnixFileMode(input, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var allowed = LinuxInputPermissionProvider.GetLinuxDeviceStatus(directory.FullName, output);
            Assert.AreEqual(InputPermissionState.Available, allowed.KeyboardMonitoring);
            Assert.AreEqual("input unchanged", File.ReadAllText(input));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    [TestCategory("PlatformLinux")]
    public void SimulationPermission_RequiresOnlyWriteAccessToUinput()
    {
        if (!OperatingSystem.IsLinux() || Environment.UserName == "root")
        {
            Assert.Inconclusive("Requires Linux file permissions without root privileges.");
            return;
        }

        var directory = Directory.CreateTempSubdirectory();
        var output = Path.Combine(directory.FullName, "uinput");
        try
        {
            File.WriteAllText(output, "output unchanged");
            File.SetUnixFileMode(output, UnixFileMode.UserWrite);
            var status = LinuxInputPermissionProvider.GetLinuxSimulationStatus(output);
            Assert.IsTrue(status.CanSimulateInput);
            Assert.AreEqual(InputPermissionState.Unknown, status.KeyboardMonitoring);

            File.SetUnixFileMode(output, UnixFileMode.UserRead);
            status = LinuxInputPermissionProvider.GetLinuxSimulationStatus(output);
            Assert.AreEqual(InputPermissionState.Denied, status.InputSimulation);
            Assert.IsFalse(status.CanSimulateInput);
            Assert.AreEqual("output unchanged", File.ReadAllText(output));

            File.Delete(output);
            Assert.AreEqual(InputPermissionState.Unavailable,
                LinuxInputPermissionProvider.GetLinuxSimulationStatus(output).InputSimulation);
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
            InputPermissionViewModel.GetPermissionText(InputPermissionState.Denied));
        Assert.AreEqual(Strings.InputPermissionUnavailable,
            InputPermissionViewModel.GetPermissionText(InputPermissionState.Unavailable));
        Assert.AreEqual(Strings.InputPermissionUnknown,
            InputPermissionViewModel.GetPermissionText(InputPermissionState.Unknown));
        var labels = Enum.GetValues<InputPermissionState>().Select(InputPermissionViewModel.GetPermissionText).ToArray();
        Assert.AreEqual(labels.Length, labels.Distinct().Count());
    }
}
