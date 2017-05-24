using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using System.Net;
using System.IO;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

       
namespace SyncClipboard
{
    public partial class MainForm : Form
    {
        [DllImport("user32.dll")]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        private static int WM_CLIPBOARDUPDATE = 0x031D;

        SyncService syncService;
        bool isSttingsFormExist = false;

        public MainForm()
        {
            InitializeComponent();
            this.TopLevel = false;
            AddClipboardFormatListener(this.Handle);
            this.LoadConfig();
            this.syncService = new SyncService(this);
            this.syncService.Start();
            notifyMenu.RenderMode = ToolStripRenderMode.System   ;
        }
        public void LoadConfig()
        {
            try
            {
                Config.Load();
            }
            catch
            { 
                MessageBox.Show("配置文件出错", "初始化默认配置(还没做，即将退出)", MessageBoxButtons.YesNo); 
                Application.Exit(); 
                return; 
            }
            if(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Program.SoftName, null) == null)
            {
                this.开机启动ToolStripMenuItem.Checked = false;
            }
            else
            {
                this.开机启动ToolStripMenuItem.Checked = true;
            }
            this.上传本机ToolStripMenuItem.Checked = Config.IfPush;
            this.下载远程ToolStripMenuItem.Checked = Config.IfPull;
        }
        public void setLog(bool notify,bool notifyIconText,string title,string content,string contentSimple,string level)
        {
            try 
            { 
                if(notify)
                {
                    this.notifyIcon1.ShowBalloonTip(5, title, content, ToolTipIcon.None);
                }
                if (notifyIconText)
                {
                    this.notifyIcon1.Text = Program.SoftName + "\n" + title + "\n" + contentSimple;
                }
                if(level == "erro")
                {
                    notifyIcon1.Icon = Properties.Resources.erro;
                }
                else if(level == "info")
                {
                    notifyIcon1.Icon = Properties.Resources.upload;
                }
            }
            catch (Exception  ex)
            {
                //Console.WriteLine("Setlog错误");
            }
        }
        private void 退出ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RemoveClipboardFormatListener(this.Handle);  
            this.syncService.Stop();
            this.notifyIcon1.Visible = false;
            Application.Exit();
        }
        protected override void DefWndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                this.syncService.StartPush();
            }
            else
            {
                base.DefWndProc(ref m);
            }
        }

        private void 设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!isSttingsFormExist)
            {
                isSttingsFormExist = true;
                new SettingsForm(this).ShowDialog();
                isSttingsFormExist = false;
            }
        }

        private void 开机启动ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (this.开机启动ToolStripMenuItem.Checked == true)
                {
                    Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Program.SoftName, Application.ExecutablePath);
                }
                else
                {
                    Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true).DeleteValue(Program.SoftName, false);
                }
            }
            catch
            {
                setLog(true, false, "设置启动项失败", "设置启动项失败", null, "warn");
            }
        }

        private void notifyIcon1_BalloonTipClicked(object sender, EventArgs e)
        {
            syncService.OpenURL();
        }

        private void 上传本机ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Config.IfPush = this.上传本机ToolStripMenuItem.Checked;
            Config.Save();
        }

        private void 下载远程ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Config.IfPull = this.下载远程ToolStripMenuItem.Checked;
            Config.Save();
        }

        private void 检查更新ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UpdateChecker updateChecker = new UpdateChecker();
            updateChecker.Check();
        }
    }
}
