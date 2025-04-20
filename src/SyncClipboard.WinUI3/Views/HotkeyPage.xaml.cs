using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
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

        private async void EditButtonClick(object sender, RoutedEventArgs _)
        {
            _viewModel.EditingHotkey = Hotkey.Nothing;
            _viewModel.EditingGuid = (Guid)((Button)sender).DataContext;
            await _HotkeyInputDialog.ShowAsync();
        }

        private void ClearButtonClick(ContentDialog _, ContentDialogButtonClickEventArgs args)
        {
            _viewModel.EditingHotkey = Hotkey.Nothing;
            _HotkeyInput.Focus(FocusState.Programmatic);
            args.Cancel = true;
        }

        private void SetToDefaultButtonClick(object sender, RoutedEventArgs _)
        {
            _viewModel.SetToDefaultCommand.Execute((Guid)((Button)sender).DataContext);
        }
    }
}
