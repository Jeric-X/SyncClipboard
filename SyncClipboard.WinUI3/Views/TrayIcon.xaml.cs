using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using SyncClipboard.Core.AbstractClasses;
using SyncClipboard.Core.Interfaces;
using System;
using System.Diagnostics;
using Windows.Foundation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views
{
    public sealed partial class TrayIcon : TaskbarIcon
    {
        // 1 MenuFlyout's Width is wrong when first poping up
        // 2 new MenuFlyoutSeparator()'s DesiredSize is (0.0, 0.0)
        // 3 new MenuFlyoutItem's DesiredSize.Width is wrong before fist poping up
        // Set a default MinWidth and Use this to tell the real Separator's height and MinWidth
        public Size SeparatorSize
        {
            get
            {
                var visibilityBackUp = _Separator.Visibility;
                _Separator.Visibility = Visibility.Visible;
                _Separator.Measure(new(10000.0, 10000.0));
                var desiredSize = _Separator.DesiredSize;
                _Separator.Visibility = visibilityBackUp;
                return desiredSize;
            }
        }

        public TrayIcon()
        {
            this.InitializeComponent();
        }

        private void MenuFlyoutItem_Click(object _, RoutedEventArgs _1)
        {
            App.Current.ExitApp();
            Environment.Exit(0);
            this.Dispose();
        }
    }
}