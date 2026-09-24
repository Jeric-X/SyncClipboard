using CommunityToolkit.Mvvm.ComponentModel;
using SharpHook.Data;
using SharpHook.Providers;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SyncClipboard.Core.Utilities.Keyboard;

/// <summary>Checks input access and requests missing macOS authorization once per instance.</summary>
public sealed class InputPermissionProvider : ObservableObject, IInputPermissionProvider
{
    private const string ApplicationServicesLibrary = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private readonly ILogger _logger;
    private readonly IThreadDispatcher _dispatcher;
    private readonly IGlobalDialog _dialog;
    private readonly Action _openAccessibilitySettings;
    private readonly Action _requestAccessibilityPermission;
    private readonly bool _isMacOS;
    private readonly Func<bool> _isAccessibilityEnabled;
    private int _accessibilityRequested;
    private int _requestInProgress;

    public bool HasRequestedAccessibilityPermission => Volatile.Read(ref _accessibilityRequested) != 0;

    public InputPermissionProvider(ILogger logger, IThreadDispatcher dispatcher, IGlobalDialog dialog)
        : this(logger, dispatcher, dialog, () =>
        {
            using var process = Process.Start(new ProcessStartInfo(
                "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")
            {
                UseShellExecute = true
            });
        }, RequestNativeAccessibilityPermission, OperatingSystem.IsMacOS())
    { }

    internal InputPermissionProvider(ILogger logger, IThreadDispatcher dispatcher, IGlobalDialog dialog,
        Action openAccessibilitySettings, Action requestAccessibilityPermission,
        bool isMacOS = true, Func<bool>? isAccessibilityEnabled = null)
    {
        _logger = logger;
        _dispatcher = dispatcher;
        _dialog = dialog;
        _openAccessibilitySettings = openAccessibilitySettings;
        _requestAccessibilityPermission = requestAccessibilityPermission;
        _isMacOS = isMacOS;
        _isAccessibilityEnabled = isAccessibilityEnabled ?? AXIsProcessTrusted;
    }

    [DllImport(ApplicationServicesLibrary)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();

    [DllImport(ApplicationServicesLibrary)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrustedWithOptions(nint options);

    [DllImport(ApplicationServicesLibrary)]
    private static extern nint CFDictionaryCreate(nint allocator, ref nint keys, ref nint values,
        nint count, nint keyCallbacks, nint valueCallbacks);

    [DllImport(ApplicationServicesLibrary)]
    private static extern void CFRelease(nint value);

    private static void RequestNativeAccessibilityPermission()
    {
        var library = NativeLibrary.Load(ApplicationServicesLibrary);
        nint options = 0;
        try
        {
            var promptKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kAXTrustedCheckOptionPrompt"));
            var trueValue = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kCFBooleanTrue"));
            var keyCallbacks = NativeLibrary.GetExport(library, "kCFTypeDictionaryKeyCallBacks");
            var valueCallbacks = NativeLibrary.GetExport(library, "kCFTypeDictionaryValueCallBacks");
            options = CFDictionaryCreate(0, ref promptKey, ref trueValue, 1, keyCallbacks, valueCallbacks);
            if (options == 0) throw new OutOfMemoryException();
            AXIsProcessTrustedWithOptions(options);
        }
        finally
        {
            if (options != 0) CFRelease(options);
            NativeLibrary.Free(library);
        }
    }

    /// <summary>Returns current access and offers to request missing authorization without waiting for the dialog.</summary>
    public bool CheckAndRequestAccessibilityPermission()
    {
        if (!_isMacOS) return true;
        if (GetPermissionState(_isAccessibilityEnabled) == InputPermissionState.Available) return true;
        if (HasRequestedAccessibilityPermission || Interlocked.CompareExchange(ref _requestInProgress, 1, 0) != 0) return false;

        DelegateExtention.SafeFireAndForget(RequestAccessibilityPermissionAsync, nameof(InputPermissionProvider));
        return false;
    }

    private async Task RequestAccessibilityPermissionAsync()
    {
        try
        {
            await _dispatcher.RunOnMainThreadAsync(async () =>
            {
                if (HasRequestedAccessibilityPermission) return;
                if (!await _dialog.ShowConfirmationAsync(Strings.AccessibilityPermission,
                    Strings.AccessibilityPermissionRequestMessage, Strings.RequestPermission, Strings.Cancel)) return;

                _openAccessibilitySettings();
                try
                {
                    _requestAccessibilityPermission();
                }
                finally
                {
                    // A native request attempt counts even if it fails; canceling the dialog does not.
                    Volatile.Write(ref _accessibilityRequested, 1);
                    OnPropertyChanged(nameof(HasRequestedAccessibilityPermission));
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Write(nameof(InputPermissionProvider), $"Failed to request accessibility permission: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _requestInProgress, 0);
        }
    }

    public InputPermissionStatus GetStatus() => GetStatus(checkKeyboardMonitoring: true);

    public InputPermissionStatus GetSimulationStatus() => GetStatus(checkKeyboardMonitoring: false);

    private InputPermissionStatus GetStatus(bool checkKeyboardMonitoring)
    {
        if (_isMacOS)
        {
            var accessibility = GetPermissionState(_isAccessibilityEnabled);
            return new(accessibility, accessibility, accessibility);
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

    private static InputPermissionState GetPermissionState(Func<bool> isAllowed)
    {
        try
        {
            return isAllowed() ? InputPermissionState.Available : InputPermissionState.Denied;
        }
        catch
        {
            return InputPermissionState.Unknown;
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
