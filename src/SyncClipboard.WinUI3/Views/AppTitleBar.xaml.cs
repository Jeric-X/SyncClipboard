using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class AppTitleBar : Grid
    {
        public event Action? NavigeMenuButtonClicked;

        public UIElement DraggableArea { get => _DraggableArea; }

        public AppTitleBar()
        {
            this.InitializeComponent();
        }

        public void HideNavigationButton()
        {
            _NavigationButton.Visibility = Visibility.Collapsed;
            _DraggableArea.Padding = new(26, 8, 0, 8);
        }

        public void ShowNavigationButton()
        {
            _NavigationButton.Visibility = Visibility.Visible;
            _DraggableArea.Padding = new(8, 8, 0, 8);
        }

        private void NavigationButton_Click(object _, RoutedEventArgs _1)
        {
            NavigeMenuButtonClicked?.Invoke();
        }
    }
}