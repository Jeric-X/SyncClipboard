using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace SyncClipboard.Core.ViewModels;

public partial class SyncSettingViewModel : ObservableObject
{
    [ObservableProperty]
    bool serverEnable;

    private readonly UserConfig _userConfig;

    public SyncSettingViewModel(UserConfig userConfig)
    {
        _userConfig = userConfig;
        _userConfig.ConfigChanged += LoadFromConfig;
        serverEnable = userConfig.Config.ServerService.SwitchOn;
    }

    private void LoadFromConfig()
    {
        ServerEnable = _userConfig.Config.ServerService.SwitchOn;
    }

    partial void OnServerEnableChanged(bool value)
    {
        _userConfig.Config.ServerService.SwitchOn = value;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        _userConfig.Save();
        base.OnPropertyChanged(e);
    }
}
