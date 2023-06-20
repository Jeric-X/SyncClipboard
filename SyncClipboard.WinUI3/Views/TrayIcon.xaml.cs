using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using SyncClipboard.Core.Interface;
using Windows.Foundation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views
{
    public sealed partial class TrayIcon : TaskbarIcon
    {
        // new MenuFlyoutSeparator()'s DesiredSize is (0.0, 0.0)
        // MenuFlyoutItem's DesiredSize.Width is wrong before fist poping up
        // Use this to tell the real Separator's height and MinWidth
        public Size SeparatorSize
        {
            get
            {
                _Separator.Visibility = Visibility.Visible;
                _Separator.Measure(new(10000.0, 10000.0));
                var desiredSize = _Separator.DesiredSize;
                _Separator.Visibility = Visibility.Collapsed;
                return desiredSize;
            }
        }

        public TrayIcon()
        {
            this.InitializeComponent();
        }

        private void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            App.Current.ExitApp();
            this.Dispose();
        }

        private void MenuFlyoutItem_Click_1(object sender, RoutedEventArgs e)
        {
            var menu = App.Current.Services.GetService<IContextMenu>();
            menu?.AddMenuItemGroup(new[]
            {
                new MenuItem { Text = "added" }
            });
        }
    }
}