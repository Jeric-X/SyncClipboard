using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
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
        private readonly AboutViewModel _aboutViewModel;

        public AboutPage()
        {
            this.InitializeComponent();
            _aboutViewModel = App.Current.Services.GetRequiredService<AboutViewModel>();
        }

        private void HyperlinkButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs _)
        {
            var url = ((HyperlinkButton)sender).Content;
            Sys.OpenWithDefaultApp(url as string);
        }

        private void DependencyItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs _)
        {
            var window = App.Current.Services.GetRequiredService<IMainWindow>();
            var settingsCard = (SettingsCard)sender;
            var path = settingsCard.Tag;

            window.NavigateToNextLevel(PageDefinition.License, path);
        }
    }
}
