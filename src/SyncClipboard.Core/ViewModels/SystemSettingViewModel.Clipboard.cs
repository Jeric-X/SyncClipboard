using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.ViewModels;

public partial class SystemSettingViewModel
{
    public bool ShowClipboardSettings { get; } = OperatingSystem.IsLinux();

    public static readonly LocaleString<ClipboardReadMethod>[] ClipboardReadMethods =
    [
        new(ClipboardReadMethod.Avalonia, I18n.Strings.BuiltIn),
        new(ClipboardReadMethod.XClip, "xclip"),
        new(ClipboardReadMethod.WlClipboard, "wl-clipboard")
    ];

    public static readonly LocaleString<ClipboardWriteMethod>[] ClipboardWriteMethods =
    [
        new(ClipboardWriteMethod.Avalonia, I18n.Strings.BuiltIn),
        new(ClipboardWriteMethod.WlClipboard, "wl-clipboard")
    ];

    private bool _isLoadingClipboardConfig;

    [ObservableProperty]
    private LocaleString<ClipboardReadMethod> clipboardReadingMethod = ClipboardReadMethods[0];
    partial void OnClipboardReadingMethodChanged(LocaleString<ClipboardReadMethod> value)
    {
        if (_isLoadingClipboardConfig) return;
        var config = _configManager.GetConfig<ClipboardFactoryConfig>();
        _configManager.SetConfig(config with { ReadMethod = value.Key });
    }

    [ObservableProperty]
    private LocaleString<ClipboardWriteMethod> clipboardWritingMethod = ClipboardWriteMethods[0];
    partial void OnClipboardWritingMethodChanged(LocaleString<ClipboardWriteMethod> value)
    {
        if (_isLoadingClipboardConfig) return;
        var config = _configManager.GetConfig<ClipboardFactoryConfig>();
        _configManager.SetConfig(config with { WriteMethod = value.Key });
    }

    private void LoadClipboardFactoryConfig(ClipboardFactoryConfig config)
    {
        _isLoadingClipboardConfig = true;
        try
        {
            ClipboardReadingMethod = LocaleString<ClipboardReadMethod>.Match(ClipboardReadMethods, config.ReadMethod);
            ClipboardWritingMethod = LocaleString<ClipboardWriteMethod>.Match(ClipboardWriteMethods, config.WriteMethod);
        }
        finally
        {
            _isLoadingClipboardConfig = false;
        }
    }
}
