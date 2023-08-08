using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using System;
using Windows.UI.WindowManagement;
using WinUIEx;
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
        private readonly SettingWindowViewModel _viewModel;

        public SettingWindow()
        {
            this.InitializeComponent();
            _viewModel = App.Current.Services.GetRequiredService<SettingWindowViewModel>();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(_AppTitleBar.DraggableArea);
            _AppTitleBar.NavigeMenuButtonClicked += () => SplitPane.IsPaneOpen = !SplitPane.IsPaneOpen;

            AppWindow.ResizeClient(new(1200, 700));
            Closed += SettingWindow_Closed;

            _MenuList.SelectedIndex = 0;
        }

        internal void NavigateTo(SettingItem item, SlideNavigationTransitionEffect effect = SlideNavigationTransitionEffect.FromBottom)
        {
            string pageName = "SyncClipboard.WinUI3.Views." + item.Name + "Page";
            Type? pageType = Type.GetType(pageName);
            SettingContentFrame.Navigate(pageType, null, new SlideNavigationTransitionInfo { Effect = effect });
        }

        private void SettingWindow_Closed(object sender, WindowEventArgs args)
        {
            this.AppWindow.Hide();
            args.Handled = true;
        }

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = ((ListView)sender).SelectedItem;
            var itemName = ((SettingItem)selectedItem).Name;
            var itemDisplayName = ((SettingItem)selectedItem).Tag;

            var item = new SettingItem(itemName, itemDisplayName);
            NavigateTo(item);

            _viewModel.BreadcrumbList.Clear();
            _viewModel.BreadcrumbList.Add(item);

            if (SplitPane.DisplayMode == SplitViewDisplayMode.Overlay)
            {
                SplitPane.IsPaneOpen = false;
            }
        }

        private void ListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (SplitPane.DisplayMode == SplitViewDisplayMode.Overlay)
            {
                SplitPane.IsPaneOpen = false;
            }

            var newItem = (SettingItem)e.ClickedItem;
            var oldItem = _viewModel.BreadcrumbList[0];

            if (newItem.Name == oldItem.Name && _viewModel.BreadcrumbList.Count > 1)
            {
                NavigateTo(oldItem, SlideNavigationTransitionEffect.FromLeft);
                _viewModel.BreadcrumbList.Clear();
                _viewModel.BreadcrumbList.Add(newItem);
            }
        }

        private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
        {
            for (int i = _viewModel.BreadcrumbList.Count - 1; i >= args.Index + 1; i--)
            {
                _viewModel.BreadcrumbList.RemoveAt(i);
            }

            NavigateTo(_viewModel.BreadcrumbList[0], SlideNavigationTransitionEffect.FromLeft);
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
            if (!this.Visible)
            {
                this.CenterOnScreen();
            }
            this.SetForegroundWindow();
        }
    }
}