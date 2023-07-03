using Microsoft.Win32;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Module;
using SyncClipboard.Utility;
using System;
using System.Linq;
using System.Windows.Forms;

namespace SyncClipboard.Control
{
    public class ContextMenu : IContextMenu
    {
        private readonly Notifyer Notifyer;
        private System.Windows.Forms.ContextMenuStrip contextMenu;
        private System.Windows.Forms.ToolStripMenuItem 开机启动MenuItem;
        private System.Windows.Forms.ToolStripMenuItem 上传本机MenuItem;
        private System.Windows.Forms.ToolStripMenuItem 下载远程MenuItem;

        public ContextMenu(Notifyer notifyer)
        {
            Notifyer = notifyer;
            InitializeComponent();
            this.LoadConfig();
        }

        private void InitializeComponent()
        {
            this.开机启动MenuItem = new System.Windows.Forms.ToolStripMenuItem("开机启动");
            this.上传本机MenuItem = new System.Windows.Forms.ToolStripMenuItem("上传本机");
            this.下载远程MenuItem = new System.Windows.Forms.ToolStripMenuItem("下载远程");

            this.开机启动MenuItem.Click += this.开机启动MenuItem_Click;
            this.上传本机MenuItem.Click += this.上传本机MenuItem_Click;
            this.下载远程MenuItem.Click += this.下载远程MenuItem_Click;

            this.contextMenu = new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new MenuStripColorTable())
            };
            this.contextMenu.Items.Add("-");
            this.contextMenu.Items.Add(this.开机启动MenuItem);
            this.contextMenu.Items.Add(this.上传本机MenuItem);
            this.contextMenu.Items.Add(this.下载远程MenuItem);

            Notifyer.SetContextMenu(this.contextMenu);
        }

        public void LoadConfig()
        {
            this.开机启动MenuItem.Checked = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Env.SoftName, null) != null;
            this.上传本机MenuItem.Checked = UserConfig.Config.SyncService.PushSwitchOn;
            this.下载远程MenuItem.Checked = UserConfig.Config.SyncService.PullSwitchOn;
        }

        private void 开机启动MenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (!this.开机启动MenuItem.Checked)
                {
                    Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", Env.SoftName, Application.ExecutablePath);
                }
                else
                {
                    Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true).DeleteValue(Env.SoftName, false);
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

        private int _index = 0;

        public void AddMenuItemGroup(string[] texts, Action[] actions)
        {
            if (texts.Length == 0)
            {
                throw new ArgumentException("参数为零");
            }

            contextMenu.Items.Insert(_index++, new ToolStripSeparator());
            for (var i = 0; i < texts.Length; i++)
            {
                var iCopy = i;
                AddMenuItem(texts[i], (_) => actions[iCopy](), false);
            }
        }

        public Action<bool>[] AddMenuItemGroup(string[] texts, Action<bool>[] actions, bool[] withCheckBox)
        {
            if (texts.Length == 0)
            {
                throw new ArgumentException("参数为零");
            }

            contextMenu.Items.Insert(_index++, new ToolStripSeparator());

            var setters = new Action<bool>[texts.Length];
            for (var i = 0; i < texts.Length; i++)
            {
                setters[i] = AddMenuItem(texts[i], actions[i], withCheckBox[i]);
            }
            return setters;
        }

        public Action<bool>[] AddMenuItemGroup(string[] texts, Action<bool>[] actions)
        {
            if (texts.Length == 0)
            {
                throw new ArgumentException("参数为零");
            }

            contextMenu.Items.Insert(_index++, new ToolStripSeparator());

            var setters = new Action<bool>[texts.Length];
            for (var i = 0; i < texts.Length; i++)
            {
                setters[i] = AddMenuItem(texts[i], actions[i], true);
            }
            return setters;
        }

        private Action<bool> AddMenuItem(string texts, Action<bool> actions, bool withCheckBox, bool reverse = false)
        {
            var item = new ToolStripMenuItem(texts)
            {
                CheckOnClick = withCheckBox
            };
            item.Click += (sender, e) => actions(item.Checked);
            contextMenu.Items.Insert(GetIndexAndAutoIncrease(reverse), item);
            return (status) => item.Checked = status;
        }

        public void AddMenuItemGroup(MenuItem[] menuItems, bool reverse = false)
        {
            if (!reverse)
                AddSeparator(reverse);

            var items = reverse ? menuItems.Reverse() : menuItems;
            foreach (var item in items)
            {
                AddMenuItem(item.Text, (_) => item.Action(), false, reverse);
            }

            if (reverse)
                AddSeparator(reverse);
        }

        private int GetIndexAndAutoIncrease(bool reverse)
        {
            if (reverse)
            {
                return _index;
            }
            return _index++;
        }

        public void AddMenuItem(MenuItem item, bool reverse = false)
        {
            AddMenuItemGroup(new MenuItem[] { item }, reverse);
        }

        private void AddSeparator(bool reverse)
        {
            if (_index != 0)
            {
                contextMenu.Items.Insert(GetIndexAndAutoIncrease(reverse), new ToolStripSeparator());
            }
        }

        public void AddMenuItemGroup(MenuItem[] menuItems)
        {
            AddMenuItemGroup(menuItems, false);
        }

        public void AddMenuItem(MenuItem menuItem)
        {
            AddMenuItem(menuItem, false);
        }
    }
}