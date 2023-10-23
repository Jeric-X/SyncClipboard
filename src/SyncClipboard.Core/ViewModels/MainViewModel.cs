using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IServiceProvider _services;

        public MainViewModel(IServiceProvider serviceProvider)
        {
            _services = serviceProvider;
        }

        public List<PageDefinition> MainWindowPage { get; } = new()
        {
            PageDefinition.SyncSetting,
            PageDefinition.CliboardAssistant,
            PageDefinition.ServiceStatus,
            PageDefinition.SystemSetting,
            PageDefinition.About,
        };

        public ObservableCollection<PageDefinition> BreadcrumbList { get; } = new();

        public void NavigateTo(PageDefinition page, NavigationTransitionEffect effect)
        {
            _services.GetService<IMainWindow>()?.NavigateTo(page, effect);
        }

        public void NavigateToLastLevel()
        {
            if (BreadcrumbList.Count > 1)
            {
                BreadcrumbList.RemoveAt(BreadcrumbList.Count - 1);
                NavigateTo(BreadcrumbList[^1], NavigationTransitionEffect.FromLeft);
            }
        }
    }
}