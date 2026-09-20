using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SharpHook.Data;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using System.Diagnostics;

namespace SyncClipboard.Core.ViewModels;

public partial class SystemSettingViewModel
{
    public bool ShowInputPermissions { get; } = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
    public bool ShowAccessibilityPermission { get; } = OperatingSystem.IsMacOS();
    public bool ShowInputDevicePermissions { get; } = OperatingSystem.IsLinux();

    public static readonly LocaleString<LinuxMode>[] LinuxInputModes =
    [
        new(LinuxMode.AutoXRecord, Strings.AutomaticInterface),
        new(LinuxMode.AutoLowLevel, "libinput/uinput"),
        new(LinuxMode.XRecord, "XRecord/XTest")
    ];

    [ObservableProperty]
    private LocaleString<LinuxMode> linuxInputMode;
    partial void OnLinuxInputModeChanged(LocaleString<LinuxMode> value) =>
        ProgramConfig = ProgramConfig with { LinuxInputMode = value.Key };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibilityPermissionText))]
    [NotifyPropertyChangedFor(nameof(KeyboardMonitoringPermissionText))]
    [NotifyPropertyChangedFor(nameof(InputSimulationPermissionText))]
    [NotifyPropertyChangedFor(nameof(CanRequestAccessibilityPermission))]
    [NotifyCanExecuteChangedFor(nameof(RequestAccessibilityPermissionCommand))]
    private InputPermissionStatus inputPermissions = new(
        InputPermissionState.Unknown, InputPermissionState.Unknown, InputPermissionState.Unknown);

    public string AccessibilityPermissionText => GetPermissionText(InputPermissions.Accessibility);
    public string KeyboardMonitoringPermissionText => GetPermissionText(InputPermissions.KeyboardMonitoring);
    public string InputSimulationPermissionText => GetPermissionText(InputPermissions.InputSimulation);
    public bool CanRequestAccessibilityPermission => ShowAccessibilityPermission &&
        InputPermissions.Accessibility is not (InputPermissionState.Available or InputPermissionState.NotRequired);

    [RelayCommand]
    public void RefreshInputPermissions()
    {
        InputPermissions = _services.GetRequiredService<IInputPermissionProvider>().GetStatus();
    }

    [RelayCommand(CanExecute = nameof(CanRequestAccessibilityPermission))]
    private async Task RequestAccessibilityPermission()
    {
        if (!CanRequestAccessibilityPermission) return;

        try
        {
            _services.GetRequiredService<IInputPermissionProvider>().RequestAccessibilityPermission();
            RefreshInputPermissions();
            if (InputPermissions.Accessibility == InputPermissionState.Denied)
            {
                // macOS may suppress repeated prompts; keep the authorization controls reachable.
                using var process = Process.Start(new ProcessStartInfo(
                    "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Write(nameof(SystemSettingViewModel), ex.Message);
            await _services.GetRequiredService<IMainWindowDialog>().ShowMessageAsync(Strings.RequestPermission, ex.Message);
        }
    }

    internal static string GetPermissionText(InputPermissionState state) => state switch
    {
        InputPermissionState.Available => Strings.InputPermissionAvailable,
        InputPermissionState.Denied => Strings.InputPermissionDenied,
        InputPermissionState.Unavailable => Strings.InputPermissionUnavailable,
        InputPermissionState.Partial => Strings.InputPermissionPartial,
        InputPermissionState.NotRequired => Strings.InputPermissionNotRequired,
        _ => Strings.InputPermissionUnknown
    };
}
