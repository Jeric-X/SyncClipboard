using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using System;
using Windows.UI.WindowManagement;
using Application = Microsoft.UI.Xaml.Application;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingWindow : Window, IMainWindow
    {
        public SettingWindow()
        {
            this.InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(_AppTitleBar.DraggableArea);
            _AppTitleBar.NavigeMenuButtonClicked += () => SplitPane.IsPaneOpen = !SplitPane.IsPaneOpen;

            AppWindow.ResizeClient(new(1200, 700));
            Closed += SettingWindow_Closed;

            _MenuList.SelectedIndex = 0;
        }

        private void SettingWindow_Closed(object sender, WindowEventArgs args)
        {
            this.AppWindow.Hide();
            args.Handled = true;
        }

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = ((ListView)sender).SelectedItem;

            string pageName = "SyncClipboard.WinUI3.Views." + (((SettingItem)selectedItem).Name + "Page");
            Type? pageType = Type.GetType(pageName);
            SettingContentFrame.Navigate(pageType ?? throw new Exception($"Page View not Found: {pageName}"));
            _SettingTitle.Text = ((SettingItem)selectedItem).Tag;

            if (SplitPane.DisplayMode == SplitViewDisplayMode.Overlay)
            {
                SplitPane.IsPaneOpen = false;
            }
        }

        private void Window_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            if (args.Size.Width < 800)
            {
                SplitPane.IsPaneOpen = false;
                SplitPane.DisplayMode = SplitViewDisplayMode.Overlay;
                _AppTitleBar.HideNavigationButton();
            }
            else
            {
                SplitPane.DisplayMode = SplitViewDisplayMode.Inline;
                SplitPane.IsPaneOpen = true;
                _AppTitleBar.ShowNavigationButton();
                SplitPane.PaneBackground = (Brush)Application.Current.Resources["LayerOnMicaBaseAltFillColorTransparentBrush"];
            }
        }

        private void SplitPane_PaneClosed(SplitView _, object _1)
        {
            SplitPane.PaneBackground = (Brush)Application.Current.Resources["LayerOnMicaBaseAltFillColorTransparentBrush"];
        }

        private void SplitPane_PaneOpening(SplitView _, object _1)
        {
            if (SplitPane.DisplayMode == SplitViewDisplayMode.Overlay)
            {
                SplitPane.PaneBackground = (Brush)Application.Current.Resources["AcrylicInAppFillColorDefaultBrush"];
            }
        }

        void IMainWindow.Show()
        {
            this.Activate();
        }
    }
}