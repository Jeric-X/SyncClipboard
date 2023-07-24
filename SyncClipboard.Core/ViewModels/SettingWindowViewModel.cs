using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels
{
    public partial class SettingWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        public ObservableCollection<SettingItem> settingItems = new();

        public SettingWindowViewModel()
        {
            SettingItems.Add(new("SyncSetting", "剪切板同步"));
            SettingItems.Add(new("NetwordSetting", "网络"));
        }
    }
}