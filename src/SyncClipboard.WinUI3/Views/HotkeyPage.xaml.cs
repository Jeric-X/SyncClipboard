using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.ViewModels;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class HotkeyPage : Page
    {
        private readonly HotkeyViewModel _viewModel;

        public HotkeyPage()
        {
            this.InitializeComponent();
            _viewModel = App.Current.Services.GetRequiredService<HotkeyViewModel>();
        }

        private async void EditButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _HotkeyInput.Hotkey = Hotkey.Nothing;
            await _HotkeyInputDialog.ShowAsync();
        }
    }
}
