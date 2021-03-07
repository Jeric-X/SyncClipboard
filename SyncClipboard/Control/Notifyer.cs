using System;
using System.Windows.Forms;

namespace SyncClipboard.Control
{
    class Notifyer
    {
        private NotifyIcon notifyIcon;
        private string notifyText;

        public Notifyer(ContextMenu contextMenu)
        {
            this.notifyIcon = new System.Windows.Forms.NotifyIcon();
            this.notifyIcon.ContextMenu = contextMenu;
            this.notifyIcon.Icon = Properties.Resources.upload;
            this.notifyIcon.Text = "SyncClipboard";
            this.notifyIcon.Visible = true;
            this.notifyIcon.BalloonTipClicked += new System.EventHandler(this.notifyIcon_BalloonTipClicked);
        }

        public void SetDoubleClickEvent(EventHandler eventHandler)
        {
            this.notifyIcon.DoubleClick += eventHandler;
        }

        public void Exit()
        {
            this.notifyIcon.Visible = false;
        }

        private void notifyIcon_BalloonTipClicked(object sender, EventArgs e)
        {
            if (notifyText == null || notifyText.Length < 4)
                return;
            if (notifyText.Substring(0, 4) == "http" || notifyText.Substring(0, 4) == "www.")
                System.Diagnostics.Process.Start(this.notifyText);
        }

        public void setLog(bool notify, bool notifyIconText, string title, string content, string contentSimple, string level)
        {
            try
            {
                if (notify)
                {
                    notifyText = content;
                    if (!string.IsNullOrEmpty(content))
                    {
                        this.notifyIcon.ShowBalloonTip(5, title, SafeMessage(content), ToolTipIcon.None);
                    }
                }
                if (notifyIconText)
                {
                    this.notifyIcon.Text = Program.SoftName + "\n" + title + "\n" + contentSimple;
                }
                if (level == "erro")
                {
                    notifyIcon.Icon = Properties.Resources.erro;
                }
                else if (level == "info")
                {
                    notifyIcon.Icon = Properties.Resources.upload;
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
    }
}
