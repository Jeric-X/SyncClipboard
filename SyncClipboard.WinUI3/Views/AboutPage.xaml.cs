using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
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
    }
}
