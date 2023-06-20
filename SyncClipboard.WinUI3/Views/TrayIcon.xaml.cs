using H.NotifyIcon;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views
{
    public sealed partial class TrayIcon : TaskbarIcon
    {
        public TrayIcon()
        {
            this.InitializeComponent();
        }

        private void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            App.Current.ExitApp();
            this.Dispose();
        }
    }
}