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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private AppWindow _appWindow;

        public MainWindow()
        {
            this.InitializeComponent();
            _appWindow = GetAppWindowForCurrentWindow();
            var titleBar = _appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            // 标题栏按键背景色设置为透明
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.Black;

            OverlappedPresenter op = _appWindow.Presenter as OverlappedPresenter;
            op.IsResizable = false;
            op.IsMaximizable = false;

            _appWindow.ResizeClient(new(900, 500));
        }

        private AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(wndId);
        }

        //C# code behind
        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var selectedItem = (Microsoft.UI.Xaml.Controls.NavigationViewItem)args.SelectedItem;
            string pageName = "SyncClipboard.WinUI3." + ((string)selectedItem.Tag);
            Type pageType = Type.GetType(pageName);
            ContentFrame.Navigate(pageType);
        }
    }
}
