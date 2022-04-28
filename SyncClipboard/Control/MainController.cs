using System;
using System.Windows.Forms;
using Microsoft.Win32;
using SyncClipboard.Utility;
using SyncClipboard.Module;

namespace SyncClipboard.Control
{
    public class MainController
    {
        public Notifyer Notifyer;
        private System.Windows.Forms.ContextMenuStrip contextMenu;
        private System.Windows.Forms.ToolStripMenuItem 退出MenuItem;
        private System.Windows.Forms.ToolStripMenuItem 设置MenuItem;
        private System.Windows.Forms.ToolStripMenuItem 开机启动MenuItem;
        private System.Windows.Forms.ToolStripMenuItem 上传本机MenuItem;
        private System.Windows.Forms.ToolStripMenuItem 下载远程MenuItem;
        private System.Windows.Forms.ToolStripMenuItem 检查更新MenuItem;
        private System.Windows.Forms.ToolStripMenuItem nextCloudLogger;

        private readonly SettingsForm settingsForm = new SettingsForm();
        private bool isSttingsFormExist = false;

        public MainController()
        {
            InitializeComponent();
            this.LoadConfig();
        }
        private void InitializeComponent()
        {
            this.设置MenuItem = new System.Windows.Forms.ToolStripMenuItem("设置");
            this.开机启动MenuItem = new System.Windows.Forms.ToolStripMenuItem("开机启动");
            this.上传本机MenuItem = new System.Windows.Forms.ToolStripMenuItem("上传本机");
            this.下载远程MenuItem = new System.Windows.Forms.ToolStripMenuItem("下载远程");
            this.退出MenuItem = new System.Windows.Forms.ToolStripMenuItem("退出");
            this.检查更新MenuItem = new System.Windows.Forms.ToolStripMenuItem("检查更新");
            this.nextCloudLogger = new System.Windows.Forms.ToolStripMenuItem("从NextCloud登录");

            this.设置MenuItem.Click += this.设置MenuItem_Click;
            this.开机启动MenuItem.Click += this.开机启动MenuItem_Click;
            this.上传本机MenuItem.Click += this.上传本机MenuItem_Click;
            this.下载远程MenuItem.Click += this.下载远程MenuItem_Click;
            this.退出MenuItem.Click += this.退出MenuItem_Click;
            this.检查更新MenuItem.Click += this.检查更新MenuItem_Click;
            this.nextCloudLogger.Click += this.NextCloudLogger_Click;

            this.contextMenu = new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new MenuStripColorTable())
            };
            this.contextMenu.Items.Add(this.设置MenuItem);
            this.contextMenu.Items.Add("-");
            this.contextMenu.Items.Add(this.nextCloudLogger);
            this.contextMenu.Items.Add("-");
            this.contextMenu.Items.Add(this.开机启动MenuItem);
            this.contextMenu.Items.Add(this.上传本机MenuItem);
            this.contextMenu.Items.Add(this.下载远程MenuItem);
            this.contextMenu.Items.Add("-");
            this.contextMenu.Items.Add(this.检查更新MenuItem);
            this.contextMenu.Items.Add(this.退出MenuItem);

            Notifyer = new Notifyer(this.contextMenu);
            Notifyer.SetDoubleClickEvent(this.设置MenuItem_Click);
        }

        public void LoadConfig()
        {
            this.开机启动MenuItem.Checked = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Program.SoftName, null) != null;
            this.上传本机MenuItem.Checked = UserConfig.Config.SyncService.PushSwitchOn;
            this.下载远程MenuItem.Checked = UserConfig.Config.SyncService.PullSwitchOn;
            settingsForm.LoadConfig();
        }

        private async void NextCloudLogger_Click(object sender, EventArgs e)
        {
            NextcloudCredential nextcloudInfo = await Nextcloud.SignInFlowAsync().ConfigureAwait(true);
            if (nextcloudInfo is null)
            {
                return;
            }

            UserConfig.Config.SyncService.UserName = nextcloudInfo.Username;
            UserConfig.Config.SyncService.Password = nextcloudInfo.Password;
            UserConfig.Config.SyncService.RemoteURL = nextcloudInfo.Url;
            UserConfig.Save();
        }

        private void 退出MenuItem_Click(object sender, EventArgs e)
        {
            UserConfig.Config.SyncService.PullSwitchOn = false;
            UserConfig.Config.SyncService.PushSwitchOn = false;
            Notifyer.Exit();
            Application.Exit();
        }
        private void 设置MenuItem_Click(object sender, EventArgs e)
        {
            if (!isSttingsFormExist)
            {
                isSttingsFormExist = true;
                settingsForm.ShowDialog();
                isSttingsFormExist = false;
            }
            else
            {
                settingsForm.Activate();
            }
        }

        private void 开机启动MenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (!this.开机启动MenuItem.Checked)
                {
                    Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Program.SoftName, Application.ExecutablePath);
                }
                else
                {
                    Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true).DeleteValue(Program.SoftName, false);
                }
                this.开机启动MenuItem.Checked = !this.开机启动MenuItem.Checked;
            }
            catch
            {
                Log.Write("设置启动项失败");
            }
        }

        private void 上传本机MenuItem_Click(object sender, EventArgs e)
        {
            this.上传本机MenuItem.Checked = !this.上传本机MenuItem.Checked;
            UserConfig.Config.SyncService.PushSwitchOn = this.上传本机MenuItem.Checked;
            UserConfig.Save();
        }

        private void 下载远程MenuItem_Click(object sender, EventArgs e)
        {
            this.下载远程MenuItem.Checked = !this.下载远程MenuItem.Checked;
            UserConfig.Config.SyncService.PullSwitchOn = this.下载远程MenuItem.Checked;
            UserConfig.Save();
        }

        private void 检查更新MenuItem_Click(object sender, EventArgs e)
        {
            UpdateChecker.Check();
        }

        public void AddMenuItem(string[] texts, Action[] actions)
        {
            if (texts.Length == 0 || texts.Length != actions.Length)
            {
                throw new ArgumentException("参数为零或不匹配");
            }

            int index = 1;
            contextMenu.Items.Insert(index++, new ToolStripSeparator());
            for (var i = 0; i < texts.Length; i++, index++) // 包括两条分割线
            {
                var iCopy = i;
                contextMenu.Items.Insert(
                    index,
                    new ToolStripMenuItem(texts[i], null, (sender, e) => actions[iCopy]())
                );
            }
        }
    }
}
