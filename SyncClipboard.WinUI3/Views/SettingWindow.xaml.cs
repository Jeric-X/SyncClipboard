using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.WinUI3.Win32;
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

            // AppWindow.SetIcon() has issue https://github.com/microsoft/microsoft-ui-xaml/issues/8134, so use P/Invoke
            this.SetWindowIcon("Assets/icon.ico");
            this.SetWindowSize(850, 530);
            Closed += SettingWindow_Closed;

            _MenuList.SelectedIndex = 0;
        }

        internal void NavigateTo(PageDefinition page,
            SlideNavigationTransitionEffect effect = SlideNavigationTransitionEffect.FromBottom,
            object? parameter = null)
        {
            string pageName = "SyncClipboard.WinUI3.Views." + page.Name + "Page";
            Type? pageType = Type.GetType(pageName);
            SettingContentFrame.Navigate(pageType, parameter, new SlideNavigationTransitionInfo { Effect = effect });
        }

        internal void NavigateToLastLevel()
        {
            if (_viewModel.BreadcrumbList.Count > 1)
            {
                _viewModel.BreadcrumbList.RemoveAt(_viewModel.BreadcrumbList.Count - 1);
                NavigateTo(_viewModel.BreadcrumbList[^1], SlideNavigationTransitionEffect.FromLeft);
            }
        }

        internal void DispableScrollViewer()
        {
            _ScrollViewer.VerticalScrollMode = ScrollMode.Disabled;
            _ScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }

        internal void EnableScrollViewer()
        {
            _ScrollViewer.VerticalScrollMode = ScrollMode.Enabled;
            _ScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }

        private void SettingWindow_Closed(object sender, WindowEventArgs args)
        {
            this.AppWindow.Hide();
            args.Handled = true;
        }

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs _)
        {
            var selectedItem = ((ListView)sender).SelectedItem;
            var page = (PageDefinition)selectedItem;

            NavigateTo(page);

            _viewModel.BreadcrumbList.Clear();
            _viewModel.BreadcrumbList.Add(page);

            if (SplitPane.DisplayMode == SplitViewDisplayMode.Overlay)
            {
                SplitPane.IsPaneOpen = false;
            }
        }

        private void ListView_ItemClick(object _, ItemClickEventArgs e)
        {
            if (SplitPane.DisplayMode == SplitViewDisplayMode.Overlay)
            {
                SplitPane.IsPaneOpen = false;
            }

            var newPage = (PageDefinition)e.ClickedItem;
            var oldPage = _viewModel.BreadcrumbList[0];

            if (newPage.Equals(oldPage) && _viewModel.BreadcrumbList.Count > 1)
            {
                NavigateTo(oldPage, SlideNavigationTransitionEffect.FromLeft);
                _viewModel.BreadcrumbList.Clear();
                _viewModel.BreadcrumbList.Add(newPage);
            }
        }

        private void BreadcrumbBar_ItemClicked(BreadcrumbBar _, BreadcrumbBarItemClickedEventArgs args)
        {
            for (int i = _viewModel.BreadcrumbList.Count - 1; i >= args.Index + 1; i--)
            {
                _viewModel.BreadcrumbList.RemoveAt(i);
            }

            NavigateTo(_viewModel.BreadcrumbList[args.Index], SlideNavigationTransitionEffect.FromLeft);
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
                this.Show();
            }
            this.SetForegroundWindow();
        }
    }
}