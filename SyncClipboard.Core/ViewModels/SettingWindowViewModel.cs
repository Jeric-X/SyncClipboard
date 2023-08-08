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
            SettingItems.Add(new("CliboardAssistant", "剪切板助手"));
            SettingItems.Add(new("ServiceStatus", "服务状态"));
            SettingItems.Add(new("SystemSetting", "系统设置"));
            SettingItems.Add(new("About", "关于"));
        }
    }
}