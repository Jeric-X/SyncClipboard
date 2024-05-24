using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.WinUI3.Win32;
using System;
using System.Drawing.Text;
using System.Linq;
using Windows.UI;
using WinUIEx;
using Application = Microsoft.UI.Xaml.Application;
using Button = SyncClipboard.Abstract.Notification.Button;

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
            this.SetWindowSize(850, 530);
            Closed += SettingWindow_Closed;

            ChangeTitleBarButtonForegroundColor((FrameworkElement)Content, null);
            ((FrameworkElement)Content).ActualThemeChanged += ChangeTitleBarButtonForegroundColor;

            _MenuList.SelectedIndex = 0;
        }

        private void OnWindowLoaded()
        {
            if (!IsIconFontInstalled())
            {
                var notifyer = App.Current.Services.GetRequiredService<INotification>();
                notifyer.SendText(
                    Strings.IconMissingDetected,
                    Strings.DownloadAndInstallIconFont,
                    new Button(Strings.Details, () => Sys.OpenWithDefaultApp("https://learn.microsoft.com/zh-cn/windows/apps/design/style/segoe-fluent-icons-font")),
                    new Button(Strings.Download, () => Sys.OpenWithDefaultApp("https://aka.ms/SegoeFluentIcons"))
                );
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
            _viewModel.BreadcrumbBarClicked(args.Index);
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
                SplitPane.PaneBackground = (AcrylicBrush)_Grid.Resources["OverlayPanBackgoundBrush"];
            }
        }

        public void Show()
        {
            if (this.DispatcherQueue.HasThreadAccess)
            {
                ShowWindow();
            }
            else
            {
                this.DispatcherQueue.TryEnqueue(ShowWindow);
            }
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

        public void ChangeTheme(string theme)
        {
            ((FrameworkElement)Content).RequestedTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        private void ChangeTitleBarButtonForegroundColor(FrameworkElement sender, object? _)
        {
            var actualTheme = sender.ActualTheme.ToString();
            var themeResource = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[actualTheme];
            AppWindow.TitleBar.ButtonForegroundColor = (Color)themeResource["TitleBarButtonForegroundColor"];
        }
    }
}