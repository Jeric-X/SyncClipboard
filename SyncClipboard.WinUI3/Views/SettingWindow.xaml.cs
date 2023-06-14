using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI.WindowManagement;
using CommunityToolkit.Mvvm.ComponentModel;
//using static System.Net.Mime.MediaTypeNames;
using CommunityToolkit.Mvvm.Input;
using Application = Microsoft.UI.Xaml.Application;

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
            //titleBar.ExtendsContentIntoTitleBar = true;

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            // 标题栏按键背景色设置为透明
            //titleBar.ButtonBackgroundColor = Colors.Transparent;
            //titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            //AppWindow.TitleBar.ButtonForegroundColor = Colors.Black;
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
            if (args.Size.Width < 800)
            {
                SplitPane.IsPaneOpen = false;
                SplitPane.DisplayMode = SplitViewDisplayMode.Overlay;
                ShowNavigeMenuButton.Visibility = Visibility.Visible;
                SplitPane.PaneBackground = (SolidColorBrush)Application.Current.Resources["LayerOnMicaBaseAltFillColorTertiaryBrush"];
                AppTitle.Margin = new(0, 0, 0, 0);
            }
            else
            {
                SplitPane.DisplayMode = SplitViewDisplayMode.Inline;
                SplitPane.IsPaneOpen = true;
                ShowNavigeMenuButton.Visibility = Visibility.Collapsed;
                SplitPane.PaneBackground = (SolidColorBrush)Application.Current.Resources["LayerOnMicaBaseAltFillColorTransparentBrush"];
                AppTitle.Margin = new(18, 0, 0, 0);
            }
        }

        [RelayCommand]
        private void ShowHideWindow()
        {
            SplitPane.IsPaneOpen = !SplitPane.IsPaneOpen;
        }
    }
}