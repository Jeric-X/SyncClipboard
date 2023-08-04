using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public class SystemSettingViewModel : ObservableObject
{
    public static string Version => "v" + Env.VERSION;

    public bool StartUpWithSystem
    {
        get => StartUpHelper.Status();
        set
        {
            StartUpHelper.Set(value);
            OnPropertyChanged(nameof(StartUpWithSystem));
        }
    }
}
