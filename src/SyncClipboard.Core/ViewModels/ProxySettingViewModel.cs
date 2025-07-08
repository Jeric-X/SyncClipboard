using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public partial class ProxySettingViewModel : ObservableObject
{
    private readonly ConfigManager configManager;

    public static readonly LocaleString<ProxyType>[] Types =
    [
        new (ProxyType.None, Strings.None),
        new (ProxyType.System, Strings.SystemProxy),
        new (ProxyType.Custom, Strings.CustomProxy)
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnableCustomAddress))]
    public LocaleString<ProxyType> type;
    public string Address { get; set; }

    public bool EnableCustomAddress => Type.Key == ProxyType.Custom;

    public ProxySettingViewModel(ConfigManager configManager)
    {
        this.configManager = configManager;

        var config = configManager.GetConfig<ProxyConfig>();
        type = Types.Match(config.Type);
        Address = config.Address;
    }

    [RelayCommand]
    private void Save()
    {
        var config = new ProxyConfig
        {
            Type = Type.Key,
            Address = Address
        };
        configManager.SetConfig(config);
    }
}
