using SharpHook.Data;
using SharpHook.Providers;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using System.Diagnostics;

namespace SyncClipboard.Core.Utilities.Keyboard;

/// <summary>Checks existing access and provides an explicit macOS authorization request.</summary>
public sealed class InputPermissionProvider : IInputPermissionProvider
{
    public async Task ResetAccessibilityPermissionAsync()
    {
        if (!OperatingSystem.IsMacOS()) return;

        using var process = Process.Start(new ProcessStartInfo(
            "/usr/bin/tccutil", "reset Accessibility xyz.jericx.desktop.syncclipboard")
        {
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Unable to start tccutil.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"tccutil exited with code {process.ExitCode}.");
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            // Do not leave a delayed reset running while the next permission request starts.
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // The process already exited.
            }
            throw new TimeoutException("Resetting accessibility permission timed out after 1 second.");
        }
    }

    public void RequestAccessibilityPermission()
    {
        if (OperatingSystem.IsMacOS())
        {
            UioHookProvider.Instance.IsAxApiEnabled(promptUserIfDisabled: true);
        }
    }

    public InputPermissionStatus GetStatus() => GetStatus(checkKeyboardMonitoring: true);

    public InputPermissionStatus GetSimulationStatus() => GetStatus(checkKeyboardMonitoring: false);

    private static InputPermissionStatus GetStatus(bool checkKeyboardMonitoring)
    {
        if (OperatingSystem.IsMacOS())
        {
            return GetAccessibilityStatus(() => UioHookProvider.Instance.IsAxApiEnabled(promptUserIfDisabled: false));
        }

        if (OperatingSystem.IsLinux())
        {
            var isWayland = string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland",
                StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
            return GetLinuxStatus(UioHookProvider.Instance, isWayland,
                () => checkKeyboardMonitoring
                    ? GetLinuxDeviceStatus("/dev/input", "/dev/uinput")
                    : GetLinuxSimulationStatus("/dev/uinput"));
        }

        return new(InputPermissionState.NotRequired, InputPermissionState.NotRequired, InputPermissionState.NotRequired);
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
