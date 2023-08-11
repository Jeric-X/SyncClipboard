using SyncClipboard.Core.AbstractClasses;
using SyncClipboard.Core.Interfaces;
using System.Windows.Forms;

namespace SyncClipboard.Control
{
    public class ContextMenu : ContextMenuBase
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

        protected override void InsertToggleMenuItem(int index, ToggleMenuItem menuitem)
        {
            var item = new ToolStripMenuItem(menuitem.Text)
            {
                CheckOnClick = true,
                Checked = menuitem.Checked,
            };

            item.Click += (sender, e) => menuitem.Action?.Invoke();
            menuitem.CheckedChanged += (bool status) => item.Checked = status;
            contextMenu.Items.Insert(index, item);
        }

        protected override void InsertMenuItem(int index, MenuItem menuitem)
        {
            var item = new ToolStripMenuItem(menuitem.Text);
            item.Click += (sender, e) => menuitem.Action?.Invoke();
            contextMenu.Items.Insert(index, item);
        }

        protected override void InsertSeparator(int index)
        {
            contextMenu.Items.Insert(index, new ToolStripSeparator());
        }

        protected override int MenuItemsCount()
        {
            return contextMenu.Items.Count;
        }
    }
}