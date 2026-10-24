using SharpHook.Data;
using SharpHook.Providers;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Permissions;

namespace SyncClipboard.Core.Utilities.Permissions;

public sealed class LinuxPermissionProvider : IPermissionProvider
{
    private readonly ILinuxBackendProvider _backend;
    private readonly Func<bool> _isWayland;
    private readonly Func<PermissionState> _getMonitoringState;
    private readonly Func<PermissionState> _getSimulationState;
    private readonly Action _showSimulationNotice;

    public LinuxPermissionProvider(IThreadDispatcher dispatcher, IGlobalDialog dialog)
        : this(UioHookProvider.Instance,
            () => string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")),
            () => GetKeyboardMonitoringState("/dev/input"), () => GetInputSimulationState("/dev/uinput"), dispatcher, dialog)
    { }

    internal LinuxPermissionProvider(ILinuxBackendProvider backend, Func<bool> isWayland,
        Func<PermissionState> getMonitoringState, Func<PermissionState> getSimulationState,
        IThreadDispatcher dispatcher, IGlobalDialog dialog)
    {
        _backend = backend;
        _isWayland = isWayland;
        _getMonitoringState = getMonitoringState;
        _getSimulationState = getSimulationState;
        _showSimulationNotice = () => DelegateExtention.SafeFireAndForget(
            () => dispatcher.RunOnMainThreadAsync(() => dialog.ShowMessageAsync(
                Strings.PermissionManagement, Strings.LinuxInputSimulationPermissionRequired, Strings.Confirm)),
            nameof(LinuxPermissionProvider));
    }

    public PermissionState GetState(PermissionCapability capability) => capability switch
    {
        PermissionCapability.SimulateInput => GetBackendState(_backend, _isWayland(), _getSimulationState),
        PermissionCapability.MonitorKeyboard => GetBackendState(_backend, _isWayland(), _getMonitoringState),
        PermissionCapability.ReadWindow or PermissionCapability.ReadCaret => PermissionState.NotRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null)
    };

    public bool CheckAndRequest(PermissionCapability capability)
    {
        if (GetState(capability).IsGranted())
            return true;
        if (capability == PermissionCapability.SimulateInput)
            _showSimulationNotice();
        return false;
    }

    public IReadOnlyList<PermissionItem> GetPermissions()
    {
        List<PermissionItem> permissions = [];
        var monitoring = GetState(PermissionCapability.MonitorKeyboard);
        if (monitoring != PermissionState.NotRequired)
            permissions.Add(new("/dev/input/event*", Strings.KeyboardMonitoringPermissionDescription, monitoring, []));
        var simulation = GetState(PermissionCapability.SimulateInput);
        if (simulation != PermissionState.NotRequired)
            permissions.Add(new("/dev/uinput", Strings.InputSimulationPermissionDescription, simulation, []));
        return permissions;
    }

    private static PermissionState GetBackendState(
        ILinuxBackendProvider provider, bool isWayland, Func<PermissionState> getDeviceState)
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
                LinuxBackend.X11 or LinuxBackend.Wayland => getDeviceState(),
                LinuxBackend.XRecord => PermissionState.NotRequired,
                _ => PermissionState.Unknown
            };
        }
        catch
        {
            return PermissionState.Unknown;
        }
    }

    internal static PermissionState GetKeyboardMonitoringState(string inputDirectory)
    {
        PermissionState monitoring;
        try
        {
            var devices = Directory.EnumerateFiles(inputDirectory, "event*")
                .Select(path => CheckDeviceAccess(path, FileAccess.ReadWrite)).ToArray();
            monitoring = devices.Length == 0 ? PermissionState.Unavailable
                : devices.All(state => state == PermissionState.Available) ? PermissionState.Available
                : devices.Any(state => state == PermissionState.Available) ? PermissionState.Partial
                : devices.Contains(PermissionState.Denied) ? PermissionState.Denied
                : devices.Contains(PermissionState.Unknown) ? PermissionState.Unknown
                : PermissionState.Unavailable;
        }
        catch (UnauthorizedAccessException)
        {
            monitoring = PermissionState.Denied;
        }
        catch (DirectoryNotFoundException)
        {
            monitoring = PermissionState.Unavailable;
        }
        catch (IOException)
        {
            monitoring = PermissionState.Unknown;
        }

        return monitoring;
    }

    internal static PermissionState GetInputSimulationState(string uinputPath) => CheckDeviceAccess(uinputPath, FileAccess.Write);

    private static PermissionState CheckDeviceAccess(string path, FileAccess access)
    {
        try
        {
            // Open and close only: never read keyboard events, write input, or create virtual devices here.
            using var handle = File.OpenHandle(path, FileMode.Open, access, FileShare.ReadWrite);
            return PermissionState.Available;
        }
        catch (UnauthorizedAccessException)
        {
            return PermissionState.Denied;
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return PermissionState.Unavailable;
        }
        catch (IOException)
        {
            return PermissionState.Unknown;
        }
    }
}
