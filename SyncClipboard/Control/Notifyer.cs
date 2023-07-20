using SyncClipboard.Core.AbstractClasses;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace SyncClipboard.Control
{
    public class Notifyer : TrayIconBase<Icon>
    {
        protected override Icon DefaultIcon => Properties.Resources.upload;
        protected override Icon ErrorIcon => Properties.Resources.erro;
        private const int MAX_NOTIFY_ICON_TIP_LETTERS = 60;
        protected override int MaxToolTipLenth => MAX_NOTIFY_ICON_TIP_LETTERS;

        private readonly NotifyIcon _notifyIcon;

        public Notifyer()
        {
            this._notifyIcon = new NotifyIcon
            {
                Icon = DefaultIcon,
                Text = "SyncClipboard",
                Visible = false
            };
            this._notifyIcon.DoubleClick += (_, _) => MainWindowWakedUp?.Invoke();
        }

        public override event Action MainWindowWakedUp;

        public void SetContextMenu(ContextMenuStrip contextMenu)
        {
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        public override void Create()
        {
            _notifyIcon.Visible = true;
        }

        protected override void SetIcon(Icon icon)
        {
            _notifyIcon.Icon = icon;
        }

        protected override void SetToolTip(string text)
        {
            _notifyIcon.Text = text;
        }

        protected override Icon[] UploadIcons()
        {
            return new Icon[]
            {
                Properties.Resources.upload001, Properties.Resources.upload002, Properties.Resources.upload003,
                Properties.Resources.upload004, Properties.Resources.upload005, Properties.Resources.upload006,
                Properties.Resources.upload007, Properties.Resources.upload008, Properties.Resources.upload009,
                Properties.Resources.upload010, Properties.Resources.upload011, Properties.Resources.upload012,
                Properties.Resources.upload013, Properties.Resources.upload014, Properties.Resources.upload015,
                Properties.Resources.upload016, Properties.Resources.upload017,
            };
        }

        protected override Icon[] DownloadIcons()
        {
            return new Icon[]
            {
                Properties.Resources.download001, Properties.Resources.download002, Properties.Resources.download003,
                Properties.Resources.download004, Properties.Resources.download005, Properties.Resources.download006,
                Properties.Resources.download007, Properties.Resources.download008, Properties.Resources.download009,
                Properties.Resources.download010, Properties.Resources.download011, Properties.Resources.download012,
                Properties.Resources.download013, Properties.Resources.download014, Properties.Resources.download015,
                Properties.Resources.download016, Properties.Resources.download017,
            };
        }
    }
}