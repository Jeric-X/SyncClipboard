using SyncClipboard.Core.AbstractClasses;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SyncClipboard.Control
{
    public class Notifyer : TrayIconBase<Icon>
    {
        private readonly Icon DefaultIcon = Properties.Resources.upload;
        private readonly Icon ErrorIcon = Properties.Resources.erro;
        private const int MAX_NOTIFY_ICON_TIP_LETTERS = 60;

        private readonly NotifyIcon _notifyIcon;
        private event Action ToastClicked;

        private Icon _staticIcon = Properties.Resources.upload;

        private readonly Dictionary<string, string> _statusList = new();

        public Notifyer()
        {
            this._notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                //ContextMenuStrip = contextMenu,
                Icon = DefaultIcon,
                Text = "SyncClipboard",
                Visible = false
            };
            this._notifyIcon.BalloonTipClicked += SetToastClickedHandler;   // to be modified
            this._notifyIcon.BalloonTipClosed += ClearToastClickedHandler;
            this._notifyIcon.DoubleClick += (_, _) => MainWindowWakedUp?.Invoke();
        }

        public override event Action MainWindowWakedUp;

        public void SetContextMenu(ContextMenuStrip contextMenu)
        {
            _notifyIcon.ContextMenuStrip = contextMenu;
        }


        public void Exit()
        {
            this._notifyIcon.Visible = false;
        }

        private void SetToastClickedHandler(object sender, EventArgs e)
        {
            ToastClicked?.Invoke();
            ClearToastClickedHandler(sender, e);
        }

        private void ClearToastClickedHandler(object sender, EventArgs e)
        {
            if (ToastClicked is null)
            {
                return;
            }

            foreach (var handler in ToastClicked.GetInvocationList())
            {
                ToastClicked -= handler as Action;
            }
        }

        private void ActiveStaticIcon()
        {
            if (!_isShowingDanamicIcon)
            {
                _notifyIcon.Icon = _staticIcon;
            }
        }

        private void ActiveStatusString()
        {
            string str = "";
            var eachMaxLenth = MAX_NOTIFY_ICON_TIP_LETTERS / _statusList.Count;

            foreach (var status in _statusList)
            {
                var oneServiceStr = $"{status.Key}: {status.Value}";
                if (oneServiceStr.Length > eachMaxLenth)
                {
                    oneServiceStr = oneServiceStr[..(eachMaxLenth - 1)];
                }
                str += oneServiceStr + System.Environment.NewLine;
            }
            this._notifyIcon.Text = str;
        }

        public void SetStatusString(string key, string statusStr, bool error)
        {
            SetStatusString(key, statusStr);

            if (error)
            {
                _staticIcon = ErrorIcon;
            }
            else
            {
                _staticIcon = DefaultIcon;
            }
            ActiveStaticIcon();
        }

        public void SetStatusString(string key, string statusStr)
        {
            if (!string.IsNullOrEmpty(key))
            {
                _statusList[key] = statusStr;
            }
            ActiveStatusString();
        }

        public override void Create()
        {
            _notifyIcon.Visible = true;
        }

        protected override void SetIcon(Icon icon)
        {
            _notifyIcon.Icon = icon;
        }

        protected override void SetDefaultIcon()
        {
            SetIcon(DefaultIcon);
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