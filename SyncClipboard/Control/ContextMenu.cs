using SyncClipboard.Core.Interfaces;
using SyncClipboard.Module;
using System;
using System.Linq;
using System.Windows.Forms;

namespace SyncClipboard.Control
{
    public class ContextMenu : IContextMenu
    {
        private readonly Notifyer Notifyer;
        private System.Windows.Forms.ContextMenuStrip contextMenu;
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
            this.上传本机MenuItem = new System.Windows.Forms.ToolStripMenuItem("上传本机");
            this.下载远程MenuItem = new System.Windows.Forms.ToolStripMenuItem("下载远程");

            this.上传本机MenuItem.Click += this.上传本机MenuItem_Click;
            this.下载远程MenuItem.Click += this.下载远程MenuItem_Click;

            this.contextMenu = new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new MenuStripColorTable())
            };
            this.contextMenu.Items.Add("-");
            this.contextMenu.Items.Add(this.上传本机MenuItem);
            this.contextMenu.Items.Add(this.下载远程MenuItem);

            Notifyer.SetContextMenu(this.contextMenu);
        }

        public void LoadConfig()
        {
            this.上传本机MenuItem.Checked = UserConfig.Config.SyncService.PushSwitchOn;
            this.下载远程MenuItem.Checked = UserConfig.Config.SyncService.PullSwitchOn;
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
                AddSingleMenuItem(item, reverse);
            }

            if (reverse)
                AddSeparator(reverse);
        }

        private void AddSingleMenuItem(MenuItem menuitem, bool reverse = false)
        {
            var item = new ToolStripMenuItem(menuitem.Text);
            if (menuitem is ToggleMenuItem toggleItem)
            {
                item.CheckOnClick = true;
                item.Checked = toggleItem.Checked;
                toggleItem.CheckedChanged += (bool status) => item.Checked = status;
            }

            item.Click += (sender, e) =>
            {
                menuitem.Action?.Invoke();
            };

            contextMenu.Items.Insert(GetIndexAndAutoIncrease(reverse), item);
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