using Microsoft.UI.Xaml;
//using Windows.UI.WindowManagement;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI;
using WinRT.Interop;
using Microsoft.UI.Windowing;
using Windows.UI.WindowManagement;
using AppWindow = Microsoft.UI.Windowing.AppWindow;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using H.NotifyIcon;
using WinRT;
using CommunityToolkit.WinUI.UI;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        Int64 handle;
        public MainWindow()
        {
            this.InitializeComponent();

            var titleBar = AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            // 标题栏按键背景色设置为透明
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.Black;

            OverlappedPresenter op = AppWindow.Presenter as OverlappedPresenter;
            op.IsResizable = true;
            op.IsMaximizable = false;

            AppWindow.ResizeClient(new(900, 500));

            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            handle = hWnd.ToInt64();
        }

        //C# code behind
        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var selectedItem = (Microsoft.UI.Xaml.Controls.NavigationViewItem)args.SelectedItem;
            string pageName = "SyncClipboard.WinUI3.Views." + ((string)selectedItem.Tag);
            Type pageType = Type.GetType(pageName);
            ContentFrame.Navigate(pageType);
            tb_doubleClicked();
        }

        private void tb_doubleClicked()
        {

        }

        private void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            TaskbarIcon.TrayIconAdd(new MenuFlyoutItem() { Text = "111111111" });
        }

        private void ToggleMenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            if (AppWindow.IsVisible)
            {
                AppWindow.Hide();
            }
            else
            {
                AppWindow.Show();
            }
        }

        private void MenuFlyoutItem_Click_1(object sender, RoutedEventArgs e)
        {
            var s = sender as MenuFlyoutItem;
            var b = s.FindParent<Frame>();
            //b.Items.Add(new MenuFlyoutItem() { Text = "MenuFlyoutItem_Click_1 added" });
        }
    }
}
