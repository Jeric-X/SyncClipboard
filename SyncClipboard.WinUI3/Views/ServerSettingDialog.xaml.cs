using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class ServerSettingDialog : ContentDialog
    {
        public string Url
        {
            get => _Url.Text;
            set => _Url.Text = value;
        }
        public string UserName
        {
            get => _UserName.Text;
            set { _UserName.Text = value; }
        }
        public string Password
        {
            get => _Password.Password;
            set => _Password.Password = value;
        }
        public string ErrorTip
        {
            get => _ErrorTip.Text;
            set => _ErrorTip.Text = value;
        }

        public string TextBoxName
        {
            set => _Url.Header = value;
        }

        public ServerSettingDialog()
        {
            this.InitializeComponent();
        }
    }
}
