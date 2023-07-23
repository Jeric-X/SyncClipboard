using SyncClipboard.Core.Interfaces;
using System;
using System.Linq;
using System.Windows.Forms;

namespace SyncClipboard.Control
{
    public class ContextMenu : IContextMenu
    {
        private readonly Notifyer Notifyer;
        private ContextMenuStrip contextMenu;

        public ContextMenu(Notifyer notifyer)
        {
            Notifyer = notifyer;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.contextMenu = new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new MenuStripColorTable())
            };

            Notifyer.SetContextMenu(this.contextMenu);
        }

        private int _index = 0;

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