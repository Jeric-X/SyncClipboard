using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels
{
    public partial class SettingWindowViewModel : ObservableObject
    {
        public ObservableCollection<SettingItem> SettingItems = new()
        {
            new ("SyncSetting", "剪切板同步"),
            new ("CliboardAssistant", "剪切板助手"),
            new ("ServiceStatus", "服务状态"),
            new ("SystemSetting", "系统设置"),
            new ("About", "关于"),
        };

        public ObservableCollection<SettingItem> BreadcrumbList { get; } = new();
    }
}