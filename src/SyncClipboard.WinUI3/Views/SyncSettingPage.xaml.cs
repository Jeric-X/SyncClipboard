using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;
using System;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SyncSettingPage : Page
    {
        private readonly SyncSettingViewModel _viewModel;

        public SyncSettingPage()
        {
            this.InitializeComponent();
            _viewModel = App.Current.Services.GetRequiredService<SyncSettingViewModel>();
            this.DataContext = _viewModel;
        }

        [RelayCommand]
        private async Task SetSyncAsync()
        {
            _SyncSettingDialog.Password = _viewModel.ClientConfig.Password;
            _SyncSettingDialog.UserName = _viewModel.ClientConfig.UserName;
            _SyncSettingDialog.Url = _viewModel.ClientConfig.RemoteURL;
            await _SyncSettingDialog.ShowAsync();
        }

        private void SyncSettingDialog_OkClick(ContentDialog _, ContentDialogButtonClickEventArgs args)
        {
            var res = _viewModel.SetClientConfig(_SyncSettingDialog.Url, _SyncSettingDialog.UserName, _SyncSettingDialog.Password);
            if (string.IsNullOrEmpty(res))
            {
                _SyncSettingDialog.ErrorTip = "";
                return;
            }

            _SyncSettingDialog.ErrorTip = res;
            args.Cancel = true;
            return;
        }
    }
}
