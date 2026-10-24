using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SharpHook.Data;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.ViewModels;

public partial class SystemSettingViewModel
{
    public bool ShowPermissions => Permissions.Count > 0;

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
    [NotifyPropertyChangedFor(nameof(ShowPermissions))]
    private IReadOnlyList<PermissionViewModel> permissions = [];

    [RelayCommand]
    public void RefreshPermissions()
    {
        Permissions = _services.GetRequiredService<IPermissionProvider>().GetPermissions()
            .Select(permission => new PermissionViewModel(permission, RefreshPermissions)).ToArray();
    }
}
