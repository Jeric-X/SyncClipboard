using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using NativeNotification.Interface;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.WinUI3.Win32;
using System;
using System.Drawing.Text;
using System.Linq;
using Windows.Graphics;
using WinUIEx;
using Application = Microsoft.UI.Xaml.Application;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window, IMainWindow
    {
        public TrayIcon TrayIcon => _TrayIcon;
        private readonly MainViewModel _viewModel;
        private bool _mainWindowLoaded = false;

        public MainWindow()
        {
            this.InitializeComponent();
            _viewModel = App.Current.Services.GetRequiredService<MainViewModel>();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(_AppTitleBar.DraggableArea);
            _AppTitleBar.NavigeMenuButtonClicked += () => SplitPane.IsPaneOpen = !SplitPane.IsPaneOpen;

            // AppWindow.SetIcon() has issue https://github.com/microsoft/microsoft-ui-xaml/issues/8134, so use P/Invoke
            this.SetWindowIcon("Assets/icon.ico");
            Closed += SettingWindow_Closed;

            this.AppWindow.Resize(new SizeInt32(_viewModel.Width, _viewModel.Height));
            this.SetTitleBarButtonForegroundColor();

            _MenuList.SelectedIndex = 0;

            App.Current.Services.GetRequiredService<ConfigManager>().GetAndListenConfig<ProgramConfig>(config =>
                this.SetTheme(config.Theme)
            );
        }

        private void OnWindowLoaded()
        {
            if (!IsIconFontInstalled())
            {
                var manager = App.Current.Services.GetRequiredService<INotificationManager>();
                var notification = manager.Create();
                notification.Title = Strings.IconMissingDetected;
                notification.Message = Strings.DownloadAndInstallIconFont;
                notification.Buttons = [
                    new ActionButton(Strings.Details, () => Sys.OpenWithDefaultApp("https://learn.microsoft.com/zh-cn/windows/apps/design/style/segoe-fluent-icons-font")),
                    new ActionButton(Strings.Download, () => Sys.OpenWithDefaultApp("https://aka.ms/SegoeFluentIcons"))
                ];
                notification.Show();
            }
        }

        private static bool IsIconFontInstalled()
        {
            using InstalledFontCollection ifc = new();
            return ifc.Families.Any(f => f.Name == "Segoe Fluent Icons");
        }

        internal void NavigateTo(PageDefinition page,
            SlideNavigationTransitionEffect effect = SlideNavigationTransitionEffect.FromBottom,
            object? parameter = null)
        {
            string pageName = "SyncClipboard.WinUI3.Views." + page.Name + "Page";
            Type? pageType = Type.GetType(pageName);
            SettingContentFrame.Navigate(pageType, parameter, new SlideNavigationTransitionInfo { Effect = effect });
            _ScrollViewer.ScrollToVerticalOffset(0);

            // Memory Leak https://github.com/microsoft/microsoft-ui-xaml/issues/5978
            GC.Collect();
        }

        public void NavigateTo(PageDefinition page, NavigationTransitionEffect effect, object? parameter = null)
        {
            SlideNavigationTransitionEffect platformEffect = effect switch
            {
                NavigationTransitionEffect.FromBottom => SlideNavigationTransitionEffect.FromBottom,
                NavigationTransitionEffect.FromLeft => SlideNavigationTransitionEffect.FromLeft,
                NavigationTransitionEffect.FromRight => SlideNavigationTransitionEffect.FromRight,
                NavigationTransitionEffect.FromTop => SlideNavigationTransitionEffect.FromBottom,
                _ => throw new NotImplementedException()
            };
            NavigateTo(page, platformEffect, parameter);
        }

        public void OpenPage(PageDefinition page, object? para)
        {
            void action()
            {
                this.Show();
                var index = _viewModel.MainWindowPage.IndexOf(page);
                if (index != -1)
                {
                    _MenuList.SelectedIndex = _viewModel.MainWindowPage.IndexOf(page);
                }
            }
            RunOnMainThread(action);
        }

        public void NavigateToLastLevel()
        {
            _viewModel.NavigateToLastLevel();
        }

        public void NavigateToNextLevel(PageDefinition page, object? para)
        {
            _viewModel.NavigateToNextLevel(page, para);
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

        private void SettingWindow_Closed(object _, WindowEventArgs args)
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
            _viewModel.BreadcrumbBarClicked(args.Index);
        }

        private void Window_SizeChanged(object _, WindowSizeChangedEventArgs args)
        {
            if (_mainWindowLoaded)
            {
                _viewModel.Height = this.AppWindow.Size.Height;
                _viewModel.Width = this.AppWindow.Size.Width;
            }

            if (args.Size.Width < 800)
            {
                SplitPane.IsPaneOpen = false;
                SplitPane.DisplayMode = SplitViewDisplayMode.Overlay;
                _AppTitleBar.ShowNavigationButton();
            }
            else
            {
                SplitPane.DisplayMode = SplitViewDisplayMode.Inline;
                SplitPane.IsPaneOpen = true;
                _AppTitleBar.HideNavigationButton();
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
                SplitPane.PaneBackground = (AcrylicBrush)_Grid.Resources["OverlayPanBackgoundBrush"];
            }
        }

        private void RunOnMainThread(Action action)
        {
            if (this.DispatcherQueue.HasThreadAccess)
            {
                action.Invoke();
            }
            else
            {
                this.DispatcherQueue.TryEnqueue(action.Invoke);
            }
        }

        public void Show()
        {
            RunOnMainThread(ShowWindow);
        }

        void ShowWindow()
        {
            if (!this.Visible)
            {
                this.CenterOnScreen();
                this.Activate();
            }
            this.SetForegroundWindow();

            if (!_mainWindowLoaded)
            {
                _mainWindowLoaded = true;
                OnWindowLoaded();
            }
        }

        public void SetFont(string font)
        {
        }

        public void ExitApp()
        {
            App.Current.ExitApp();
        }
    }
}