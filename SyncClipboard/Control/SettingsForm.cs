using System;
using System.Windows.Forms;
using SyncClipboard.Control;

namespace SyncClipboard
{
    public partial class SettingsForm : Form
    {
        private MainController mainController;
        private SettingsForm()
        {
            InitializeComponent();
        }
        public SettingsForm(MainController mainform)
        {
            InitializeComponent();
            mainController = mainform;
            this.textBox6.KeyPress += OnlyNum;
            this.textBox7.KeyPress += OnlyNum;
            this.textBox8.KeyPress += OnlyNum;
        }

        private void OnlyNum(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar != '\b' && !Char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }

        private void SettingsForm_Load(object sender, EventArgs e)
        {
            this.textBox1.Text = UserConfig.Config.SyncService.RemoteURL;
            this.textBox2.Text = UserConfig.Config.SyncService.UserName;
            this.textBox3.Text = UserConfig.Config.SyncService.Password;
            this.textBox6.Text = UserConfig.Config.Program.RetryTimes.ToString();
            this.textBox7.Text = (UserConfig.Config.Program.TimeOut / 1000).ToString();
            this.textBox8.Text = (UserConfig.Config.Program.IntervalTime / 1000).ToString();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            SaveConfig();
            this.Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            SaveConfig();
        }
        private void SaveConfig()
        {
            UserConfig.Config.SyncService.RemoteURL = this.textBox1.Text;
            UserConfig.Config.SyncService.UserName = this.textBox2.Text;
            UserConfig.Config.SyncService.Password = this.textBox3.Text;

            if (this.textBox8.Text != "")
                UserConfig.Config.Program.IntervalTime = Convert.ToInt32(this.textBox8.Text) * 1000;
            if (this.textBox7.Text != "")
                UserConfig.Config.Program.TimeOut = Convert.ToInt32(this.textBox7.Text) * 1000;
            if (this.textBox6.Text != "")
                UserConfig.Config.Program.RetryTimes = Convert.ToInt32(this.textBox6.Text);
            Config.Save();
        }
    }
}
