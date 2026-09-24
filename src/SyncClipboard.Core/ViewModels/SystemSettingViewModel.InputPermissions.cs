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
    public bool ShowInputPermissions => ShowAccessibilityPermission || (ShowInputDevicePermissions &&
        (InputPermissions.KeyboardMonitoring != InputPermissionState.NotRequired ||
         InputPermissions.InputSimulation != InputPermissionState.NotRequired));
    public bool ShowAccessibilityPermission { get; } = OperatingSystem.IsMacOS();
    public bool ShowInputDevicePermissions { get; } = OperatingSystem.IsLinux();

    public static readonly LocaleString<LinuxMode>[] LinuxInputModes =
    [
        new(LinuxMode.AutoXRecord, Strings.AutomaticInterface),
        new(LinuxMode.XRecord, "x11"),
        new(LinuxMode.AutoLowLevel, "libinput/uinput")
    ];

    public bool ShowGlobalHotkeyEngine { get; } = OperatingSystem.IsLinux();

    public string LinuxInputModeDescription => LinuxInputMode.Key switch
    {
        LinuxMode.XRecord => Strings.GlobalHotkeyEngineX11Description,
        LinuxMode.AutoLowLevel => Strings.GlobalHotkeyEngineLibinputDescription,
        _ => Strings.GlobalHotkeyEngineAutomaticDescription
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LinuxInputModeDescription))]
    private LocaleString<LinuxMode> linuxInputMode;
    partial void OnLinuxInputModeChanged(LocaleString<LinuxMode> value) =>
        ProgramConfig = ProgramConfig with { LinuxInputMode = value.Key };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInputPermissions))]
    [NotifyPropertyChangedFor(nameof(AccessibilityPermissionText))]
    [NotifyPropertyChangedFor(nameof(IsAccessibilityPermissionAvailable))]
    [NotifyPropertyChangedFor(nameof(KeyboardMonitoringPermissionText))]
    [NotifyPropertyChangedFor(nameof(InputSimulationPermissionText))]
    private InputPermissionStatus inputPermissions = new(
        InputPermissionState.Unknown, InputPermissionState.Unknown, InputPermissionState.Unknown);

    public string AccessibilityPermissionText => GetPermissionText(InputPermissions.Accessibility);
    public bool IsAccessibilityPermissionAvailable => InputPermissions.Accessibility == InputPermissionState.Available;
    public string KeyboardMonitoringPermissionText => GetPermissionText(InputPermissions.KeyboardMonitoring);
    public string InputSimulationPermissionText => GetPermissionText(InputPermissions.InputSimulation);
    public IInputPermissionProvider InputPermissionProvider => _services.GetRequiredService<IInputPermissionProvider>();

    [RelayCommand]
    public void RefreshInputPermissions()
    {
        InputPermissions = InputPermissionProvider.GetStatus();
    }

    [RelayCommand(CanExecute = nameof(ShowAccessibilityPermission))]
    private void RequestAccessibilityPermission()
    {
        if (!ShowAccessibilityPermission) return;

        InputPermissionProvider.CheckAndRequestAccessibilityPermission();
        RefreshInputPermissions();
    }

    [RelayCommand(CanExecute = nameof(ShowAccessibilityPermission))]
    private async Task OpenAccessibilitySettings()
    {
        if (!ShowAccessibilityPermission) return;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(
                "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Write(nameof(SystemSettingViewModel), ex.Message);
            await _services.GetRequiredService<IMainWindowDialog>().ShowMessageAsync(Strings.OpenSystemSettings, ex.Message);
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
