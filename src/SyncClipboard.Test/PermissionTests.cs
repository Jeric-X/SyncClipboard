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
using SyncClipboard.Core.Models.Permissions;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.Utilities.Permissions;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test;

[TestClass]
public class PermissionTests
{
    public TestContext TestContext { get; set; } = null!;

    internal static MacOSPermissionProvider CreatePermissionProvider(ILogger logger, Action requestAccessibilityPermission,
        Func<bool>? isAccessibilityEnabled = null,
        Func<Task<bool>>? confirm = null, Action? openSettings = null, Func<Task>? resetPermission = null,
        Func<bool>? canPostEvents = null, Action<string>? onConfirmation = null)
    {
        var dispatcher = new Mock<IThreadDispatcher>();
        dispatcher.Setup(x => x.RunOnMainThreadAsync(It.IsAny<Func<Task>>()))
            .Returns<Func<Task>>(action => action());
        var dialog = new Mock<IGlobalDialog>();
        dialog.Setup(x => x.ShowConfirmationAsync(Strings.AccessibilityPermission,
            It.IsAny<string>(), Strings.RequestPermission, Strings.Cancel))
            .Callback<string, string, string, string>((_, message, _, _) => onConfirmation?.Invoke(message))
            .Returns(() => confirm?.Invoke() ?? Task.FromResult(true));
        return new MacOSPermissionProvider(logger, dispatcher.Object, dialog.Object,
            openSettings ?? (() => { }), requestAccessibilityPermission, resetPermission ?? (() => Task.CompletedTask),
            isAccessibilityEnabled ?? (() => false), canPostEvents ?? isAccessibilityEnabled ?? (() => false));
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void AccessibilityConfirmation_CoalescesRequests_AndCancellationAllowsRetry(bool ax, bool cg)
    {
        var capability = ax ? PermissionCapability.SimulateInput : PermissionCapability.ReadWindow;
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

        Parallel.For(0, 10, _ => Assert.IsFalse(provider.CheckAndRequest(capability)));
        Assert.HasCount(1, events);
        confirmation.SetResult(false);
        Assert.HasCount(1, events);

        confirmation = new TaskCompletionSource<bool>();
        Assert.IsFalse(provider.CheckAndRequest(capability));
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

        Assert.IsFalse(provider.CheckAndRequest(PermissionCapability.SimulateInput));
        granted = true;
        confirmation.SetResult(true);

        Assert.AreEqual(0, resets);
        Assert.AreEqual(0, requests);
        Assert.AreEqual(0, settings);
        Assert.IsTrue(provider.CheckAndRequest(PermissionCapability.SimulateInput));

        granted = false;
        Assert.IsFalse(provider.CheckAndRequest(PermissionCapability.SimulateInput));
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

        Parallel.For(0, 10, _ => Assert.IsFalse(provider.CheckAndRequest(PermissionCapability.SimulateInput)));
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

        Assert.IsFalse(provider.CheckAndRequest(PermissionCapability.SimulateInput));
        string[] expected = ["dialog", "reset", "settings", "request"];
        CollectionAssert.AreEqual(expected, events);
        logger.Verify(x => x.Write(nameof(MacOSPermissionProvider),
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
        var provider = PermissionTests.CreatePermissionProvider(logger.Object, () =>
        {
            requests++;
            if (failure == "request")
                throw new InvalidOperationException("Request failed");
        }, isAccessibilityEnabled: () => false, openSettings: () =>
        {
            if (failure == "settings")
                throw new InvalidOperationException("Settings failed");
        });

        Assert.IsFalse(provider.CheckAndRequest(PermissionCapability.SimulateInput));
        Assert.AreEqual(failure == "settings" ? 0 : 1, requests);
        logger.Verify(x => x.Write(nameof(MacOSPermissionProvider), It.IsAny<string>()),
            failure == "none" ? Times.Never() : Times.Once());
    }

    [TestMethod]
    public void WindowsPermissions_RequireNoAuthorizationOrSettingsEntries()
    {
        var provider = new WindowsPermissionProvider();
        foreach (var capability in Enum.GetValues<PermissionCapability>())
        {
            Assert.IsTrue(provider.CheckAndRequest(capability));
            Assert.AreEqual(PermissionState.NotRequired, provider.GetState(capability));
        }
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
        var permissions = new LinuxPermissionProvider(provider.Object, () => isWayland,
            () => throw new AssertFailedException("Simulation must not scan keyboard devices."), () =>
            {
                deviceChecks++;
                return PermissionState.Available;
            }, Mock.Of<IThreadDispatcher>(), Mock.Of<IGlobalDialog>());
        var simulator = new Mock<IEventSimulator>();
        var created = 0;
        using var keyboard = new VirtualKeyboard(permissions, () =>
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
        var permissions = new LinuxPermissionProvider(provider.Object, () => isWayland,
            () => PermissionState.Denied, () => PermissionState.Denied,
            Mock.Of<IThreadDispatcher>(), Mock.Of<IGlobalDialog>());
        Assert.AreEqual(PermissionState.Denied, permissions.GetState(PermissionCapability.MonitorKeyboard));
        Assert.IsFalse(permissions.GetState(PermissionCapability.SimulateInput).IsGranted());
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
            var permissions = new Mock<IPermissionProvider>();
            var viewModel = new ClipboardOwnerFilterSettingViewModel(config, Mock.Of<IClipboardChangingListener>(), permissions.Object);
            viewModel.UseConfig(configKey);
            config.SetConfig(configKey, new ClipboardOwnerFilterConfig { FilterMode = "WhiteList" });
            permissions.Verify(x => x.CheckAndRequest(PermissionCapability.ReadWindow), Times.Never);

            viewModel.FilterMode = ClipboardOwnerFilterSettingViewModel.Modes[0];
            permissions.Verify(x => x.CheckAndRequest(PermissionCapability.ReadWindow), Times.Never);
            viewModel.FilterMode = ClipboardOwnerFilterSettingViewModel.Modes[1];
            permissions.Verify(x => x.CheckAndRequest(PermissionCapability.ReadWindow), Times.Once);
            viewModel.FilterMode = ClipboardOwnerFilterSettingViewModel.Modes[2];
            permissions.Verify(x => x.CheckAndRequest(PermissionCapability.ReadWindow), Times.Once);
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
            var permissions = new Mock<IPermissionProvider>();
            var viewModel = new HotkeyBlacklistViewModel(config,
                new ForegroundWindowCapture(registry, Mock.Of<INativeWindowController>()),
                new HotkeyManager(registry, config), permissions.Object);
            config.SetConfig(new HotkeyBlacklistConfig { Enabled = false });
            config.SetConfig(new HotkeyBlacklistConfig { Enabled = true });
            permissions.Verify(x => x.CheckAndRequest(PermissionCapability.ReadWindow), Times.Never);

            viewModel.IsEnabled = false;
            permissions.Verify(x => x.CheckAndRequest(PermissionCapability.ReadWindow), Times.Never);
            viewModel.IsEnabled = true;
            viewModel.IsEnabled = true;
            permissions.Verify(x => x.CheckAndRequest(PermissionCapability.ReadWindow), Times.Once);
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
            var permissions = new Mock<IPermissionProvider>(MockBehavior.Strict);
            var state = PermissionState.Denied;
            var operations = 0;
            IReadOnlyList<PermissionItem> GetPermissions() =>
            [new("Permission", "Description", state, [new("Authorize", () =>
            {
                operations++;
                state = PermissionState.Available;
                return Task.CompletedTask;
            })])];
            permissions.Setup(x => x.GetPermissions()).Returns(GetPermissions);
            using var services = new ServiceCollection()
                .AddSingleton(permissions.Object)
                .AddSingleton(Mock.Of<ILogger>())
                .BuildServiceProvider();
            var config = new ConfigManager(Path.Combine(directory.FullName, "config.json"), migrationServices.Upgrader);
            var viewModel = new SystemSettingViewModel(config, new StaticConfig(Mock.Of<INotificationManager>()), services);

            viewModel.RefreshPermissions();
            Assert.IsTrue(viewModel.ShowPermissions);
            var item = viewModel.Permissions.Single();
            Assert.AreEqual("Permission", item.Name);
            Assert.AreEqual("Description", item.Description);
            Assert.AreEqual(Strings.InputPermissionDenied, item.StateText);
            Assert.AreEqual(0, operations, "Refreshing must never request authorization.");

            var action = item.Actions.Single();
            Assert.AreEqual("Authorize", action.Text);
            await action.Command.ExecuteAsync(null);
            Assert.AreEqual(1, operations);
            Assert.AreEqual(Strings.InputPermissionAvailable, viewModel.Permissions.Single().StateText);

            permissions.Setup(x => x.GetPermissions()).Returns([]);
            viewModel.RefreshPermissions();
            Assert.IsFalse(viewModel.ShowPermissions);
            Assert.IsEmpty(viewModel.Permissions);
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
        var provider = new LinuxPermissionProvider(native.Object, () => false, () =>
        {
            deviceChecks++;
            return PermissionState.Partial;
        }, () => PermissionState.Denied, Mock.Of<IThreadDispatcher>(), Mock.Of<IGlobalDialog>());

        Assert.AreEqual(backend == LinuxBackend.XRecord ? PermissionState.NotRequired : PermissionState.Denied,
            provider.GetState(PermissionCapability.SimulateInput));
        Assert.AreEqual(0, deviceChecks, "Simulation should not scan keyboard-monitoring devices.");
        var entries = provider.GetPermissions();
        Assert.HasCount(count, entries);
        if (count > 0)
        {
            Assert.AreEqual("/dev/input/event*", entries[0].Name);
            Assert.AreEqual(PermissionState.Partial, entries[0].State);
            Assert.AreEqual("/dev/uinput", entries[1].Name);
            Assert.AreEqual(PermissionState.Denied, entries[1].State);
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
        Assert.AreEqual(PermissionState.Partial, entry.State);
        Assert.IsEmpty(events);
        Assert.AreEqual(Strings.RequestPermission, entry.Actions.Single().Text);
        await entry.Actions.Single().ExecuteAsync();
        string[] requestedEvents = ["confirm", "reset", "settings", "request"];
        CollectionAssert.AreEqual(requestedEvents, events);

        granted = true;
        events.Clear();
        entry = provider.GetPermissions().Single();
        Assert.AreEqual(PermissionState.Available, entry.State);
        Assert.AreEqual(Strings.OpenSystemSettings, entry.Actions.Single().Text);
        await entry.Actions.Single().ExecuteAsync();
        string[] settingsEvents = ["settings"];
        CollectionAssert.AreEqual(settingsEvents, events);
    }

    [TestMethod]
    public void MacCapabilities_RecheckCurrentAccess_WithoutRequesting()
    {
        var trusted = false;
        var provider = CreatePermissionProvider(Mock.Of<ILogger>(),
            () => Assert.Fail("Status checks must not request permission."), isAccessibilityEnabled: () => trusted);

        foreach (var granted in new[] { false, true, false })
        {
            trusted = granted;
            foreach (var capability in Enum.GetValues<PermissionCapability>())
            {
                var expected = capability == PermissionCapability.MonitorKeyboard ? PermissionState.NotRequired
                    : granted ? PermissionState.Available : PermissionState.Denied;
                Assert.AreEqual(expected, provider.GetState(capability));
                if (granted)
                    Assert.IsTrue(provider.CheckAndRequest(capability));
            }
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void MacCapabilities_GatePostChecksOnAX_AndKeepWindowAccessIndependent(bool ax, bool cg)
    {
        var cgChecks = 0;
        var provider = CreatePermissionProvider(Mock.Of<ILogger>(),
            () => Assert.Fail("Status checks must not request permission."),
            isAccessibilityEnabled: () => ax, canPostEvents: () =>
            {
                cgChecks++;
                return cg;
            });

        var expected = !ax ? PermissionState.Denied : cg ? PermissionState.Available : PermissionState.Partial;
        Assert.AreEqual(expected, provider.GetState(PermissionCapability.SimulateInput));
        Assert.AreEqual(ax ? 1 : 0, cgChecks);
        foreach (var capability in new[] { PermissionCapability.ReadWindow, PermissionCapability.ReadCaret })
            Assert.AreEqual(ax ? PermissionState.Available : PermissionState.Denied, provider.GetState(capability));
        Assert.IsTrue(provider.CheckAndRequest(PermissionCapability.MonitorKeyboard));
        Assert.AreEqual(PermissionState.NotRequired, provider.GetState(PermissionCapability.MonitorKeyboard));
        Assert.AreEqual(ax ? 1 : 0, cgChecks);
        Assert.AreEqual(expected, provider.GetPermissions().Single().State);
        Assert.AreEqual(ax ? 2 : 0, cgChecks);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FailedNativeCheck_OnlyAffectsDependentMacCapabilities(bool axFails)
    {
        var provider = CreatePermissionProvider(Mock.Of<ILogger>(), () => { },
            isAccessibilityEnabled: () => axFails ? throw new DllNotFoundException() : true,
            canPostEvents: () => axFails ? true : throw new DllNotFoundException());
        Assert.AreEqual(PermissionState.Unknown, provider.GetState(PermissionCapability.SimulateInput));
        foreach (var capability in new[] { PermissionCapability.ReadWindow, PermissionCapability.ReadCaret })
            Assert.AreEqual(axFails ? PermissionState.Unknown : PermissionState.Available, provider.GetState(capability));
        Assert.AreEqual(PermissionState.NotRequired, provider.GetState(PermissionCapability.MonitorKeyboard));
        Assert.AreEqual(PermissionState.Unknown, provider.GetPermissions().Single().State);
    }

    [TestMethod]
    [DataRow(PermissionCapability.ReadWindow)]
    [DataRow(PermissionCapability.ReadCaret)]
    public void RequestedCapabilityGrantedDuringConfirmation_DoesNotResetForUnrelatedDeniedPermission(
        PermissionCapability capability)
    {
        var confirmation = new TaskCompletionSource<bool>();
        var granted = false;
        var requests = 0;
        var resets = 0;
        var provider = CreatePermissionProvider(Mock.Of<ILogger>(), () => requests++,
            isAccessibilityEnabled: () => granted,
            canPostEvents: () => false,
            confirm: () => confirmation.Task,
            resetPermission: () =>
            {
                resets++;
                return Task.CompletedTask;
            });

        Assert.IsFalse(provider.CheckAndRequest(capability));
        granted = true;
        confirmation.SetResult(true);
        Assert.AreEqual(0, resets);
        Assert.AreEqual(0, requests);
        Assert.IsTrue(provider.CheckAndRequest(capability));
        Assert.AreEqual(PermissionState.Partial, provider.GetPermissions().Single().State);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task MacAuthorization_ExplainsRestartOnlyWhenCGDeniesAccess(bool axGranted, bool fromSettings)
    {
        var messages = new List<string>();
        var cgChecks = 0;
        var events = new List<string>();
        var provider = CreatePermissionProvider(Mock.Of<ILogger>(), () => events.Add("request"),
            isAccessibilityEnabled: () => axGranted, canPostEvents: () =>
            {
                cgChecks++;
                return false;
            }, onConfirmation: messages.Add, openSettings: () => events.Add("settings"), resetPermission: () =>
            {
                events.Add("reset");
                return Task.CompletedTask;
            });

        if (fromSettings)
            await provider.GetPermissions().Single().Actions.Single().ExecuteAsync();
        else
            Assert.IsFalse(provider.CheckAndRequest(PermissionCapability.SimulateInput));

        var expected = Strings.AccessibilityPermissionRequestMessage;
        if (axGranted)
            expected += $"{Environment.NewLine}{Environment.NewLine}{Strings.AccessibilityPermissionRestartRequiredMessage}";
        Assert.AreEqual(expected, messages.Single());
        Assert.AreEqual(axGranted, cgChecks > 0);
        string[] expectedEvents = ["reset", "settings", "request"];
        CollectionAssert.AreEqual(expectedEvents, events);
    }

    [TestMethod]
    public void InvalidCapabilities_AreRejectedByEveryPlatform()
    {
        IPermissionProvider[] providers = [new WindowsPermissionProvider(),
            CreatePermissionProvider(Mock.Of<ILogger>(), () => Assert.Fail("Must not request access.")),
            new LinuxPermissionProvider(Mock.Of<ILinuxBackendProvider>(), () => false,
                () => PermissionState.Available, () => PermissionState.Available,
                Mock.Of<IThreadDispatcher>(), Mock.Of<IGlobalDialog>())];
        foreach (var provider in providers)
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => provider.GetState((PermissionCapability)999));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => provider.CheckAndRequest((PermissionCapability)999));
        }
    }

    [TestMethod]
    public void MissingDevices_AreUnavailable_AndAreNotCreated()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Assert.AreEqual(PermissionState.Unavailable, LinuxPermissionProvider.GetKeyboardMonitoringState(directory));
        Assert.AreEqual(PermissionState.Unavailable, LinuxPermissionProvider.GetInputSimulationState(Path.Combine(directory, "uinput")));
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
            Assert.AreEqual(PermissionState.Available, LinuxPermissionProvider.GetKeyboardMonitoringState(directory.FullName));
            Assert.AreEqual(PermissionState.Available, LinuxPermissionProvider.GetInputSimulationState(output));
            Assert.AreEqual("input unchanged", File.ReadAllText(input));
            Assert.AreEqual("output unchanged", File.ReadAllText(output));

            File.Delete(output);
            Assert.AreEqual(PermissionState.Available, LinuxPermissionProvider.GetKeyboardMonitoringState(directory.FullName));
            Assert.AreEqual(PermissionState.Unavailable, LinuxPermissionProvider.GetInputSimulationState(output));
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
        try
        {
            var input = Path.Combine(directory.FullName, "event0");
            File.WriteAllText(input, "input unchanged");
            File.SetUnixFileMode(input, UnixFileMode.UserRead);
            Assert.AreEqual(PermissionState.Denied, LinuxPermissionProvider.GetKeyboardMonitoringState(directory.FullName));

            File.SetUnixFileMode(input, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.AreEqual(PermissionState.Available, LinuxPermissionProvider.GetKeyboardMonitoringState(directory.FullName));
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
            Assert.IsTrue(LinuxPermissionProvider.GetInputSimulationState(output).IsGranted());

            File.SetUnixFileMode(output, UnixFileMode.UserRead);
            Assert.AreEqual(PermissionState.Denied, LinuxPermissionProvider.GetInputSimulationState(output));
            Assert.AreEqual("output unchanged", File.ReadAllText(output));

            File.Delete(output);
            Assert.AreEqual(PermissionState.Unavailable, LinuxPermissionProvider.GetInputSimulationState(output));
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
            PermissionViewModel.GetPermissionText(PermissionState.Denied));
        Assert.AreEqual(Strings.InputPermissionUnavailable,
            PermissionViewModel.GetPermissionText(PermissionState.Unavailable));
        Assert.AreEqual(Strings.InputPermissionUnknown,
            PermissionViewModel.GetPermissionText(PermissionState.Unknown));
        var labels = Enum.GetValues<PermissionState>().Select(PermissionViewModel.GetPermissionText).ToArray();
        Assert.AreEqual(labels.Length, labels.Distinct().Count());
    }
}
