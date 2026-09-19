using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using System.Diagnostics;

namespace SyncClipboard.Core.ViewModels;

public partial class SystemSettingViewModel
{
    public bool ShowInputPermissions => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
    public bool ShowAccessibilityPermission => OperatingSystem.IsMacOS();
    public bool ShowInputDevicePermissions => OperatingSystem.IsLinux();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibilityPermissionText))]
    [NotifyPropertyChangedFor(nameof(KeyboardMonitoringPermissionText))]
    [NotifyPropertyChangedFor(nameof(InputSimulationPermissionText))]
    private InputPermissionStatus inputPermissions = new(
        InputPermissionState.Unknown, InputPermissionState.Unknown, InputPermissionState.Unknown);

    public string AccessibilityPermissionText => GetPermissionText(InputPermissions.Accessibility);
    public string KeyboardMonitoringPermissionText => GetPermissionText(InputPermissions.KeyboardMonitoring);
    public string InputSimulationPermissionText => GetPermissionText(InputPermissions.InputSimulation);

    [RelayCommand]
    public void RefreshInputPermissions()
    {
        InputPermissions = _services.GetRequiredService<IInputPermissionProvider>().GetStatus();
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
