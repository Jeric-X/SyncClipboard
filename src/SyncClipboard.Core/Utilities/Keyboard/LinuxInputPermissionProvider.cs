using SharpHook.Data;
using SharpHook.Providers;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Core.Utilities.Keyboard;

public sealed class LinuxInputPermissionProvider : IInputPermissionProvider
{
    private readonly ILinuxBackendProvider _backend;
    private readonly Func<bool> _isWayland;
    private readonly Func<InputPermissionStatus> _getDeviceStatus;
    private readonly Func<InputPermissionStatus> _getSimulationStatus;

    public LinuxInputPermissionProvider() : this(UioHookProvider.Instance,
        () => string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")),
        () => GetLinuxDeviceStatus("/dev/input", "/dev/uinput"), () => GetLinuxSimulationStatus("/dev/uinput"))
    { }

    internal LinuxInputPermissionProvider(ILinuxBackendProvider backend, Func<bool> isWayland,
        Func<InputPermissionStatus> getDeviceStatus, Func<InputPermissionStatus> getSimulationStatus)
    {
        _backend = backend;
        _isWayland = isWayland;
        _getDeviceStatus = getDeviceStatus;
        _getSimulationStatus = getSimulationStatus;
    }

    public InputPermissionStatus GetStatus() => GetLinuxStatus(_backend, _isWayland(), _getDeviceStatus);

    public InputPermissionStatus GetSimulationStatus() => GetLinuxStatus(_backend, _isWayland(), _getSimulationStatus);

    public bool CheckAndRequestAccessibilityPermission() => true;

    public IReadOnlyList<InputPermissionItem> GetPermissions()
    {
        var status = GetStatus();
        List<InputPermissionItem> permissions = [];
        if (status.KeyboardMonitoring != InputPermissionState.NotRequired)
            permissions.Add(new("/dev/input/event*", Strings.KeyboardMonitoringPermissionDescription, status.KeyboardMonitoring, []));
        if (status.InputSimulation != InputPermissionState.NotRequired)
            permissions.Add(new("/dev/uinput", Strings.InputSimulationPermissionDescription, status.InputSimulation, []));
        return permissions;
    }

    internal static InputPermissionStatus GetLinuxStatus(
        ILinuxBackendProvider provider, bool isWayland, Func<InputPermissionStatus> getDeviceStatus)
    {
        try
        {
            var backend = provider.GetLoadedLinuxBackend();
            if (backend == LinuxBackend.None)
            {
                // No hook or simulator may have loaded the backend yet. Use the current process mode,
                // not a saved setting that takes effect after restart, to allow the first simulation.
                backend = provider.GetLinuxMode() switch
                {
                    LinuxMode.XRecord => LinuxBackend.XRecord,
                    LinuxMode.X11 => LinuxBackend.X11,
                    LinuxMode.Wayland => LinuxBackend.Wayland,
                    LinuxMode.AutoXRecord => isWayland ? LinuxBackend.Wayland : LinuxBackend.XRecord,
                    LinuxMode.AutoLowLevel => isWayland ? LinuxBackend.Wayland : LinuxBackend.X11,
                    _ => LinuxBackend.None
                };
            }

            return backend switch
            {
                LinuxBackend.X11 or LinuxBackend.Wayland => getDeviceStatus(),
                LinuxBackend.XRecord => new(InputPermissionState.NotRequired, InputPermissionState.NotRequired, InputPermissionState.NotRequired),
                _ => new(InputPermissionState.NotRequired, InputPermissionState.Unknown, InputPermissionState.Unknown)
            };
        }
        catch
        {
            return new(InputPermissionState.NotRequired, InputPermissionState.Unknown, InputPermissionState.Unknown);
        }
    }

    internal static InputPermissionStatus GetLinuxDeviceStatus(string inputDirectory, string uinputPath)
    {
        InputPermissionState monitoring;
        try
        {
            var devices = Directory.EnumerateFiles(inputDirectory, "event*")
                .Select(path => CheckDeviceAccess(path, FileAccess.ReadWrite)).ToArray();
            monitoring = devices.Length == 0 ? InputPermissionState.Unavailable
                : devices.All(state => state == InputPermissionState.Available) ? InputPermissionState.Available
                : devices.Any(state => state == InputPermissionState.Available) ? InputPermissionState.Partial
                : devices.Contains(InputPermissionState.Denied) ? InputPermissionState.Denied
                : devices.Contains(InputPermissionState.Unknown) ? InputPermissionState.Unknown
                : InputPermissionState.Unavailable;
        }
        catch (UnauthorizedAccessException)
        {
            monitoring = InputPermissionState.Denied;
        }
        catch (DirectoryNotFoundException)
        {
            monitoring = InputPermissionState.Unavailable;
        }
        catch (IOException)
        {
            monitoring = InputPermissionState.Unknown;
        }

        return GetLinuxSimulationStatus(uinputPath) with { KeyboardMonitoring = monitoring };
    }

    internal static InputPermissionStatus GetLinuxSimulationStatus(string uinputPath) => new(
        InputPermissionState.NotRequired, InputPermissionState.Unknown, CheckDeviceAccess(uinputPath, FileAccess.Write));

    private static InputPermissionState CheckDeviceAccess(string path, FileAccess access)
    {
        try
        {
            // Open and close only: never read keyboard events, write input, or create virtual devices here.
            using var handle = File.OpenHandle(path, FileMode.Open, access, FileShare.ReadWrite);
            return InputPermissionState.Available;
        }
        catch (UnauthorizedAccessException)
        {
            return InputPermissionState.Denied;
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return InputPermissionState.Unavailable;
        }
        catch (IOException)
        {
            return InputPermissionState.Unknown;
        }
    }
}
