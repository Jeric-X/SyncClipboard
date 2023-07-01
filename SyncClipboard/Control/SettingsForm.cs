using System;
using System.ComponentModel;
using System.Windows.Forms;
using SyncClipboard.Core.Interfaces;

namespace SyncClipboard
{
    public partial class SettingsForm : Form, IMainWindow
    {
        private readonly Core.Commons.UserConfig _userConfig;

        public SettingsForm(Core.Commons.UserConfig userConfig)
        {
            InitializeComponent();
            this.textBox6.KeyPress += OnlyNum;
            this.textBox7.KeyPress += OnlyNum;
            this.textBox8.KeyPress += OnlyNum;
            _userConfig = userConfig;
            LoadConfig();
            _userConfig.ConfigChanged += LoadConfig;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            this.Hide();
            e.Cancel = true;
        }

        private void OnlyNum(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar != '\b' && !Char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }

        private void LoadConfig()
        {
            this.textBox1.Text = _userConfig.Config.SyncService.RemoteURL;
            this.textBox2.Text = _userConfig.Config.SyncService.UserName;
            this.textBox3.Text = _userConfig.Config.SyncService.Password;
            this.textBox6.Text = _userConfig.Config.Program.RetryTimes.ToString();
            this.textBox7.Text = _userConfig.Config.Program.TimeOut.ToString();
            this.textBox8.Text = _userConfig.Config.Program.IntervalTime.ToString();
        }

        private void OKButtenClicked(object sender, EventArgs e)
        {
            SaveConfig();
            this.Close();
        }

        private void CancelButtonClicked(object sender, EventArgs e)
        {
            this.Close();
        }

        private void ApplicationButtonClicked(object sender, EventArgs e)
        {
            SaveConfig();
        }
        private void SaveConfig()
        {
            _userConfig.Config.SyncService.RemoteURL = this.textBox1.Text;
            _userConfig.Config.SyncService.UserName = this.textBox2.Text;
            _userConfig.Config.SyncService.Password = this.textBox3.Text;

            if (this.textBox8.Text != "")
                _userConfig.Config.Program.IntervalTime = Convert.ToInt32(this.textBox8.Text);
            if (this.textBox7.Text != "")
                _userConfig.Config.Program.TimeOut = Convert.ToInt32(this.textBox7.Text);
            if (this.textBox6.Text != "")
                _userConfig.Config.Program.RetryTimes = Convert.ToInt32(this.textBox6.Text);
            _userConfig.Save();
        }
    }
}
