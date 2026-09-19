using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test;

[TestClass]
public class InputPermissionTests
{
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
