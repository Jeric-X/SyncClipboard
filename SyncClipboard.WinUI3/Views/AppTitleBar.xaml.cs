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
    public sealed partial class AppTitleBar : StackPanel
    {
        public event Action? NavigeMenuButtonClicked;

        public UIElement DraggableArea { get => _DraggableArea; }

        public AppTitleBar()
        {
            this.InitializeComponent();
        }

        public void ShowNavigationButton()
        {
            _NavigationButton.Visibility = Visibility.Collapsed;
            _DraggableArea.Margin = new(18, 0, 0, 0);
        }

        public void HideNavigationButton()
        {
            _NavigationButton.Visibility = Visibility.Visible;
            _DraggableArea.Margin = new(0, 0, 0, 0);
        }

        private void NavigationButton_Click(object sender, RoutedEventArgs e)
        {
            NavigeMenuButtonClicked?.Invoke();
        }
    }
}