using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SyncClipboard
{
    public partial class SettingsForm : Form
    {
        private MainForm mainForm;
        public SettingsForm()
        {
            InitializeComponent();
        }
        public SettingsForm(MainForm mainform)
        {
            InitializeComponent();
            mainForm = mainform;
        }

        private void SettingsForm_Load(object sender, EventArgs e)
        {
            this.textBox1.Text = Config.RemoteURL;
            this.textBox2.Text = Config.User;
            this.textBox3.Text = Config.Password;

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
            Properties.Settings.Default.URL = this.textBox1.Text;
            Properties.Settings.Default.USERNAME = this.textBox2.Text;
            Properties.Settings.Default.PASSWORD = this.textBox3.Text;
            Properties.Settings.Default.Save();
            mainForm.LoadConfig();
        }
    }
}
