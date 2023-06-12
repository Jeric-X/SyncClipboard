using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingWindow : Window
    {
        public SettingWindow()
        {
            this.InitializeComponent();

            var titleBar = AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            // 标题栏按键背景色设置为透明
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.Black;
            AppWindow.ResizeClient(new(1200, 700));
        }

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = ((ListView)sender).SelectedItem;
            string pageName = "SyncClipboard.WinUI3.Views." + (((ListViewItem)selectedItem).Tag);
            Type? pageType = Type.GetType(pageName);
            SettingContentFrame.Navigate(pageType ?? throw new Exception($"Page View not Found: {pageName}"));
        }

        private void Window_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            if (args.Size.Width < 600)
            {
                MenuList.Visibility = Visibility.Collapsed;
                Console.WriteLine(MenuList.Width);
                //MainGrid.ColumnDefinitions[0].Width = new GridLength(0, GridUnitType.Pixel);
            }
            else
            {
                //MainGrid.ColumnDefinitions[0].Width = new GridLength(200, GridUnitType.Pixel);
                MenuList.Visibility = Visibility.Visible;
            }
        }
    }
}