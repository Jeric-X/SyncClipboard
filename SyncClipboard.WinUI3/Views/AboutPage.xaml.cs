using CommunityToolkit.Labs.WinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class AboutPage : Page
    {
        private readonly SystemSettingViewModel _SettingViewModel;
        private readonly AboutViewModel _aboutViewModel;

        public AboutPage()
        {
            this.InitializeComponent();
            _SettingViewModel = App.Current.Services.GetRequiredService<SystemSettingViewModel>();
            _aboutViewModel = App.Current.Services.GetRequiredService<AboutViewModel>();
        }

        private void HyperlinkButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs _)
        {
            var url = ((HyperlinkButton)sender).Content;
            Sys.OpenWithDefaultApp(url as string);
        }

        private void DependencyItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs _)
        {
            var window = App.Current.Services.GetService<IMainWindow>() as SettingWindow;
            var settingsCard = (SettingsCard)sender;
            var path = $"{settingsCard.Header}/{settingsCard.Tag}";

            var mainWindowVM = App.Current.Services.GetService<SettingWindowViewModel>();
            mainWindowVM?.BreadcrumbList.Add(PageDefinition.License);
            window?.NavigateTo(PageDefinition.License, SlideNavigationTransitionEffect.FromRight, path);
        }
    }
}
