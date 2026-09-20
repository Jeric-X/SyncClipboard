using SharpHook.Data;
using SharpHook.Providers;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Core.Utilities.Keyboard;

/// <summary>Checks existing access without requesting authorization or changing device permissions.</summary>
public sealed class InputPermissionProvider : IInputPermissionProvider
{
    public InputPermissionStatus GetStatus()
    {
        if (OperatingSystem.IsMacOS())
        {
            return GetAccessibilityStatus(() => UioHookProvider.Instance.IsAxApiEnabled(promptUserIfDisabled: false));
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var backend = UioHookProvider.Instance.GetLoadedLinuxBackend();
                // XRecord is the default X11 backend; the separate X11 backend uses libinput/uinput like Wayland.
                var needsDeviceAccess = backend is LinuxBackend.Wayland or LinuxBackend.X11 ||
                    (backend == LinuxBackend.None && IsWaylandSession());
                if (needsDeviceAccess)
                {
                    return GetLinuxDeviceStatus("/dev/input", "/dev/uinput");
                }
            }
            catch
            {
                return new(InputPermissionState.NotRequired, InputPermissionState.Unknown, InputPermissionState.Unknown);
            }
        }

        return new(InputPermissionState.NotRequired, InputPermissionState.NotRequired, InputPermissionState.NotRequired);
    }

    private static bool IsWaylandSession() =>
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    internal static InputPermissionStatus GetAccessibilityStatus(Func<bool> isTrusted)
    {
        InputPermissionState state;
        try
        {
            state = isTrusted() ? InputPermissionState.Available : InputPermissionState.Denied;
        }
        catch
        {
            state = InputPermissionState.Unknown;
        }
        return new(state, state, state);
    }

    internal static InputPermissionStatus GetLinuxDeviceStatus(string inputDirectory, string uinputPath)
    {
        InputPermissionState monitoring;
        try
        {
            var devices = Directory.EnumerateFiles(inputDirectory, "event*")
                .Select(path => CheckDeviceAccess(path, FileAccess.Read)).ToArray();
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

        return new(InputPermissionState.NotRequired, monitoring, CheckDeviceAccess(uinputPath, FileAccess.Write));
    }

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
