using System;
using System.Drawing;
using System.Windows.Forms;

namespace SyncClipboard.Control
{
    class Notifyer
    {
        private NotifyIcon _notifyIcon;
        private string _notifyText;

        private System.Timers.Timer _iconTimer;
        private Icon[] _dynamicIcons;
        private static Icon _defaultIcon = Properties.Resources.upload;
        private int _iconIndex = 1;

        public Notifyer(ContextMenu contextMenu)
        {
            this._notifyIcon = new System.Windows.Forms.NotifyIcon();
            this._notifyIcon.ContextMenu = contextMenu;
            this._notifyIcon.Icon = _defaultIcon;
            this._notifyIcon.Text = "SyncClipboard";
            this._notifyIcon.Visible = true;
            this._notifyIcon.BalloonTipClicked += new System.EventHandler(this.notifyIcon_BalloonTipClicked);
        }

        public void SetDoubleClickEvent(EventHandler eventHandler)
        {
            this._notifyIcon.DoubleClick += eventHandler;
        }

        public void Exit()
        {
            this._notifyIcon.Visible = false;
        }

        private void notifyIcon_BalloonTipClicked(object sender, EventArgs e)
        {
            if (_notifyText == null || _notifyText.Length < 4)
                return;
            if (_notifyText.Substring(0, 4) == "http" || _notifyText.Substring(0, 4) == "www.")
                System.Diagnostics.Process.Start(this._notifyText);
        }

        public void setLog(bool notify, bool notifyIconText, string title, string content, string contentSimple, string level)
        {
            try
            {
                if (notify)
                {
                    _notifyText = content;
                    if (!string.IsNullOrEmpty(content))
                    {
                        this._notifyIcon.ShowBalloonTip(5, title, SafeMessage(content), ToolTipIcon.None);
                    }
                }
                if (notifyIconText)
                {
                    this._notifyIcon.Text = Program.SoftName + "\n" + title + "\n" + contentSimple;
                }
                if (level == "erro")
                {
                    _notifyIcon.Icon = Properties.Resources.erro;
                }
                else if (level == "info")
                {
                    _notifyIcon.Icon = Properties.Resources.upload;
                }
            }
            catch (Exception)
            {
                //Log.Write("Setlog错误");
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
            _iconTimer.Start();
        }

        public void StopDynamicNotifyIcon()
        {
            _iconTimer.Stop();
            _iconTimer.Close();
            _iconTimer = null;

            _dynamicIcons = null;
            _iconIndex = 1;
            _notifyIcon.Icon = _defaultIcon;
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
    }
}
