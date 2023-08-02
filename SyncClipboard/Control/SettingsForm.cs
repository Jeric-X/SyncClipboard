using System;
using System.ComponentModel;
using System.Windows.Forms;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.Models;

namespace SyncClipboard
{
    public partial class SettingsForm : Form, IMainWindow
    {
        private readonly UserConfig2 _userConfig;
        private SyncConfig _syncConfig;
        private ChangeableAppConfig _appConfig;

        public SettingsForm(UserConfig2 userConfig)
        {
            InitializeComponent();
            this.textBox6.KeyPress += OnlyNum;
            this.textBox7.KeyPress += OnlyNum;
            this.textBox8.KeyPress += OnlyNum;
            _userConfig = userConfig;
            LoadSyncConfig(_userConfig.GetConfig<SyncConfig>(SyncService.CONFIG_KEY));
            LoadAppConfig(_userConfig.GetConfig<ChangeableAppConfig>("Program"));
            _userConfig.ListenConfig<SyncConfig>(SyncService.CONFIG_KEY, LoadSyncConfig);
            _userConfig.ListenConfig<ChangeableAppConfig>("Program", LoadAppConfig);
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

        private void LoadSyncConfig(object newConfig)
        {
            _syncConfig = newConfig as SyncConfig ?? new();
            this.textBox1.Text = _syncConfig.RemoteURL;
            this.textBox2.Text = _syncConfig.UserName;
            this.textBox3.Text = _syncConfig.Password;
        }

        private void LoadAppConfig(object newConfig)
        {
            _appConfig = newConfig as ChangeableAppConfig ?? new();
            this.textBox6.Text = _appConfig.RetryTimes.ToString();
            this.textBox7.Text = _appConfig.TimeOut.ToString();
            this.textBox8.Text = _appConfig.IntervalTime.ToString();
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
            _syncConfig.RemoteURL = this.textBox1.Text;
            _syncConfig.UserName = this.textBox2.Text;
            _syncConfig.Password = this.textBox3.Text;
            _userConfig.SetConfig(SyncService.CONFIG_KEY, _syncConfig);

            if (this.textBox8.Text != "")
                _appConfig.IntervalTime = Convert.ToInt32(this.textBox8.Text);
            if (this.textBox7.Text != "")
                _appConfig.TimeOut = Convert.ToInt32(this.textBox7.Text);
            if (this.textBox6.Text != "")
                _appConfig.RetryTimes = Convert.ToInt32(this.textBox6.Text);
            _userConfig.SetConfig("Program", _appConfig);
        }
    }
}
