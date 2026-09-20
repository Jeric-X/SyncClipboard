using Microsoft.Extensions.DependencyInjection;
using Moq;
using NativeNotification.Interface;
using SharpHook.Data;
using SharpHook.Providers;
using SharpHook.Simulation;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test;

[TestClass]
public class InputPermissionTests
{
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
        permissions.Setup(x => x.GetStatus()).Returns(() => InputPermissionProvider.GetLinuxStatus(
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
    public async Task AccessibilityRequest_RefreshesStatus_AndButtonTracksGrantAndRevocation()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var permissions = new Mock<IInputPermissionProvider>();
            var state = InputPermissionState.Denied;
            permissions.Setup(x => x.GetStatus()).Returns(() => new InputPermissionStatus(state, state, state));
            permissions.Setup(x => x.RequestAccessibilityPermission()).Callback(() => state = InputPermissionState.Available);
            using var services = new ServiceCollection()
                .AddSingleton(permissions.Object)
                .AddSingleton(Mock.Of<ILogger>())
                .BuildServiceProvider();
            var config = new ConfigManager(Path.Combine(directory.FullName, "config.json"), new SyncClipboardConfigUpgrader());
            var viewModel = new SystemSettingViewModel(config, new StaticConfig(Mock.Of<INotificationManager>()), services);
            var propertyChanges = new List<string?>();
            var commandChanges = 0;
            viewModel.PropertyChanged += (_, e) => propertyChanges.Add(e.PropertyName);
            viewModel.RequestAccessibilityPermissionCommand.CanExecuteChanged += (_, _) => commandChanges++;

            viewModel.RefreshInputPermissions();
            Assert.AreEqual(OperatingSystem.IsMacOS(), viewModel.CanRequestAccessibilityPermission);
            permissions.Verify(x => x.RequestAccessibilityPermission(), Times.Never);

            if (OperatingSystem.IsMacOS())
            {
                await viewModel.RequestAccessibilityPermissionCommand.ExecuteAsync(null);
                permissions.Verify(x => x.RequestAccessibilityPermission(), Times.Once);
            }
            else
            {
                state = InputPermissionState.Available;
                viewModel.RefreshInputPermissions();
            }

            Assert.AreEqual(InputPermissionState.Available, viewModel.InputPermissions.Accessibility);
            Assert.IsFalse(viewModel.CanRequestAccessibilityPermission);
            Assert.IsFalse(viewModel.RequestAccessibilityPermissionCommand.CanExecute(null));
            Assert.Contains(nameof(SystemSettingViewModel.CanRequestAccessibilityPermission), propertyChanges);
            Assert.IsGreaterThan(0, commandChanges);

            state = InputPermissionState.Denied;
            viewModel.RefreshInputPermissions();
            Assert.AreEqual(OperatingSystem.IsMacOS(), viewModel.CanRequestAccessibilityPermission);
            Assert.AreEqual(OperatingSystem.IsMacOS(), viewModel.RequestAccessibilityPermissionCommand.CanExecute(null));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void AccessibilityCheck_ReflectsGrantAndRevocation()
    {
        var trusted = false;
        var denied = InputPermissionProvider.GetAccessibilityStatus(() => trusted);
        Assert.AreEqual(InputPermissionState.Denied, denied.Accessibility);
        Assert.IsFalse(denied.CanSimulateInput);

        trusted = true;
        var granted = InputPermissionProvider.GetAccessibilityStatus(() => trusted);
        Assert.AreEqual(InputPermissionState.Available, granted.Accessibility);
        Assert.IsTrue(granted.CanSimulateInput);

        trusted = false;
        Assert.IsFalse(InputPermissionProvider.GetAccessibilityStatus(() => trusted).CanSimulateInput);
    }

    [TestMethod]
    public void FailedAccessibilityCheck_IsUnknownInsteadOfGrantedOrDenied()
    {
        var status = InputPermissionProvider.GetAccessibilityStatus(() => throw new DllNotFoundException());
        Assert.AreEqual(InputPermissionState.Unknown, status.Accessibility);
        Assert.IsFalse(status.CanSimulateInput);
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
