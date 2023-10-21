using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        public List<PageDefinition> MainWindowPage = new()
        {
            PageDefinition.SyncSetting,
            PageDefinition.CliboardAssistant,
            PageDefinition.ServiceStatus,
            PageDefinition.SystemSetting,
            PageDefinition.About,
        };

        public ObservableCollection<PageDefinition> BreadcrumbList { get; } = new();
    }
}