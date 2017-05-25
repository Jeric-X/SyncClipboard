using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            this.textBox1.Text = Config.RemoteURL;
            this.textBox2.Text = Config.User;
            this.textBox3.Text = Config.Password;
            this.textBox4.Text = Config.CustomName;
            this.textBox6.Text = Config.RetryTimes.ToString();
            this.textBox7.Text = (Config.TimeOut / 1000).ToString();
            this.textBox8.Text = (Config.IntervalTime / 1000).ToString();

            if(Config.IsCustomServer)
                this.radioButton1.Checked = true;
            else
                this.radioButton2.Checked = true;
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
            Config.RemoteURL = this.textBox1.Text;
            Config.User = this.textBox2.Text;
            Config.Password = this.textBox3.Text;
            Config.IsCustomServer = this.radioButton1.Checked;
            Config.CustomName = this.textBox4.Text;

            if (this.textBox8.Text != "")
                Config.IntervalTime = Convert.ToInt32(this.textBox8.Text) * 1000;
            if (this.textBox7.Text != "")
                Config.TimeOut = Convert.ToInt32(this.textBox7.Text) * 1000;
            if (this.textBox6.Text != "")
                Config.RetryTimes = Convert.ToInt32(this.textBox6.Text);
            Config.Save();
            mainController.LoadConfig();
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton1.Checked)
            {
                panel1.Visible = true;
                panel2.Visible = false;
            }
            else
            {
                panel2.Visible = true;
                panel1.Visible = false;
            }
        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {
            textBox5.Text = Program.DefaultServer + textBox4.Text + ".json";
        }
    }
}
