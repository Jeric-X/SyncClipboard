using Avalonia.Controls;
using FluentAvalonia.UI.Controls;

namespace SyncClipboard.Desktop.Views
{
    public partial class ServerSettingDialog : UserControl
    {
        public string Url
        {
            get => _Url.Text ?? "";
            set => _Url.Text = value;
        }
        public string UserName
        {
            get => _UserName.Text ?? "";
            set { _UserName.Text = value; }
        }
        public string Password
        {
            get => _Password.Text ?? "";
            set => _Password.Text = value;
        }
        public string ErrorTip
        {
            get => _ErrorTip.Text ?? "";
            set => _ErrorTip.Text = value;
        }

        public string TextBoxName
        {
            set => _UrlTitle.Text = value;
        }

        public ServerSettingDialog()
        {
            this.InitializeComponent();
        }
    }
}
