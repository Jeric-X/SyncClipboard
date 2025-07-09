using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels
{
    public partial class MainViewModel(
        IServiceProvider services,
        [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig) : ObservableObject
    {
        public ObservableCollection<PageDefinition> MainWindowPage { get; } =
        [
            PageDefinition.SyncSetting,
            PageDefinition.CliboardAssistant,
            PageDefinition.ServiceStatus,
            PageDefinition.Hotkey,
            PageDefinition.SystemSetting,
            PageDefinition.About,
        ];

        public ObservableCollection<PageDefinition> BreadcrumbList { get; } = [];

        public void NavigateTo(PageDefinition page, NavigationTransitionEffect effect, object? para = null)
        {
            services.GetService<IMainWindow>()?.NavigateTo(page, effect, para);
        }

        public void NavigateToLastLevel()
        {
            if (BreadcrumbList.Count > 1)
            {
                BreadcrumbList.RemoveAt(BreadcrumbList.Count - 1);
                NavigateTo(BreadcrumbList[^1], NavigationTransitionEffect.FromLeft);
            }
        }

        public void NavigateToNextLevel(PageDefinition page, object? para = null)
        {
            BreadcrumbList.Add(page);
            NavigateTo(page, NavigationTransitionEffect.FromRight, para);
        }

        public void BreadcrumbBarClicked(int index)
        {
            if (BreadcrumbList.Count - 1 == index)
            {
                return;
            }

            for (int i = BreadcrumbList.Count - 1; i >= index + 1; i--)
            {
                BreadcrumbList.RemoveAt(i);
            }

            NavigateTo(BreadcrumbList[index], NavigationTransitionEffect.FromLeft);
        }


        public int Width
        {
            get => runtimeConfig.GetConfig<MainWindowConfig>().Width;
            set => runtimeConfig.SetConfig(runtimeConfig.GetConfig<MainWindowConfig>() with { Width = value });
        }

        public int Height
        {
            get => runtimeConfig.GetConfig<MainWindowConfig>().Height;
            set => runtimeConfig.SetConfig(runtimeConfig.GetConfig<MainWindowConfig>() with { Height = value });
        }
    }
}