using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SyncClipboard.Control
{
    public class Notifyer
    {
        private readonly Icon DefaultIcon = Properties.Resources.upload;
        private readonly Icon ErrorIcon = Properties.Resources.erro;
        private const int MAX_NOTIFY_ICON_TIP_LETTERS = 60;

        private NotifyIcon _notifyIcon;
        private event Action _toastClicked;
        private string _notifyText; // to be modified

        private System.Timers.Timer _iconTimer;
        private Icon[] _dynamicIcons;
        private Icon _staticIcon = Properties.Resources.upload;
        private int _iconIndex = 1;
        private bool _isShowingDanamicIcon = false;

        private Dictionary<string, string> _statusList = new Dictionary<string, string>();

        public Notifyer(ContextMenu contextMenu)
        {
            this._notifyIcon = new System.Windows.Forms.NotifyIcon();
            this._notifyIcon.ContextMenu = contextMenu;
            this._notifyIcon.Icon = DefaultIcon;
            this._notifyIcon.Text = "SyncClipboard";
            this._notifyIcon.Visible = true;
            this._notifyIcon.BalloonTipClicked += SetToastClickedHandler;   // to be modified
            this._notifyIcon.BalloonTipClosed += ClearToastClickedHandler;
        }

        public void SetDoubleClickEvent(EventHandler eventHandler)
        {
            this._notifyIcon.DoubleClick += eventHandler;
        }

        public void Exit()
        {
            this._notifyIcon.Visible = false;
        }

        private void SetToastClickedHandler(object sender, EventArgs e)
        {
            _toastClicked?.Invoke();
            ClearToastClickedHandler(sender, e);
        }

        private void ClearToastClickedHandler(object sender, EventArgs e)
        {
            if (_toastClicked is null)
            {
                return;
            }

            foreach (var handler in _toastClicked.GetInvocationList())
            {
                _toastClicked -= handler as Action;
            }
        }

        private String SafeMessage(String str)
        {
            if (str == null)
            {
                return "【非文本类型】";
            }
            if (str.Length > 42)
            {
                return str.Substring(0, 40) + "...";
            }

            return str;
        }

        public void SetDynamicNotifyIcon(Icon[] icons, int delayTime)
        {
            if (icons.Length <= 0)
            {
                return;
            }

            _dynamicIcons = icons;
            _notifyIcon.Icon = _dynamicIcons[0];
            _iconTimer = new System.Timers.Timer(delayTime);
            _iconTimer.Elapsed += SetNextDynamicNotifyIcon;
            _iconTimer.AutoReset = true;
            _isShowingDanamicIcon = true;
            _iconTimer.Start();
        }

        public void StopDynamicNotifyIcon()
        {
            _iconTimer.Stop();
            _iconTimer.Close();
            _iconTimer = null;

            _dynamicIcons = null;
            _iconIndex = 1;
            _isShowingDanamicIcon = false;
            ActiveStaticIcon();
        }

        private void ActiveStaticIcon()
        {
            if (!_isShowingDanamicIcon)
            {
                _notifyIcon.Icon = _staticIcon;
            }
        }

        private void SetNextDynamicNotifyIcon(object sender, EventArgs e)
        {
            if (_dynamicIcons is null || _dynamicIcons.Length == 0)
            {
                return;
            }

            if (_iconIndex >= _dynamicIcons.Length)
            {
                _iconIndex = 0;
            }

            _notifyIcon.Icon = _dynamicIcons[_iconIndex];
            _iconIndex++;
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
                    oneServiceStr = oneServiceStr.Substring(0, eachMaxLenth - 1);
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

        public void ToastNotify(string title, string content, Action eventHandler = null)
        {
            const int durationTime = 5;

            if (!string.IsNullOrEmpty(content))
            {
                if (eventHandler != null)
                {
                    _toastClicked += eventHandler;
                }

                this._notifyIcon.ShowBalloonTip(durationTime, title, SafeMessage(content), ToolTipIcon.None);
            }
        }
    }
}