using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
using SyncClipboard.Core.Interfaces;
using Windows.Foundation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views
{
    public sealed partial class TrayIcon : TaskbarIcon, ITrayIcon
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

        public Window? MainWindow { get; set; }

        public TrayIcon()
        {
            this.InitializeComponent();
            DoubleClickCommand = new RelayCommand(() => MainWindow?.Activate());
        }

        private void MenuFlyoutItem_Click(object _, RoutedEventArgs _1)
        {
            App.Current.ExitApp();
            this.Dispose();
        }

        void ITrayIcon.Create()
        {
            this.ForceCreate();
        }
    }
}