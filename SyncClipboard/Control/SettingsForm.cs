using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace SyncClipboard
{
    public partial class SettingsForm : Form, IMainWindow
    {
        private readonly ConfigManager _configManager;
        private SyncConfig _syncConfig;

        public SettingsForm(ConfigManager configManager)
        {
            InitializeComponent();
            this.textBox6.KeyPress += OnlyNum;
            this.textBox7.KeyPress += OnlyNum;
            this.textBox8.KeyPress += OnlyNum;
            _configManager = configManager;
            LoadSyncConfig(_configManager.GetConfig<SyncConfig>(ConfigKey.Sync));
            _configManager.ListenConfig<SyncConfig>(ConfigKey.Sync, LoadSyncConfig);
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
            this.textBox6.Text = _syncConfig.RetryTimes.ToString();
            this.textBox7.Text = _syncConfig.TimeOut.ToString();
            this.textBox8.Text = _syncConfig.IntervalTime.ToString();
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

            if (this.textBox8.Text != "")
                _syncConfig.IntervalTime = Convert.ToUInt32(this.textBox8.Text);
            if (this.textBox7.Text != "")
                _syncConfig.TimeOut = Convert.ToUInt32(this.textBox7.Text);
            if (this.textBox6.Text != "")
                _syncConfig.RetryTimes = Convert.ToInt32(this.textBox6.Text);
            _configManager.SetConfig(ConfigKey.Sync, _syncConfig);
        }
    }
}
