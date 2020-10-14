using System;
using System.Threading;
using System.Windows.Forms;
using SyncClipboard.Control;

namespace SyncClipboard
{
    public class PullService
    {
        private Notify Notify;
        private bool switchOn = false;
        private bool clipboardChanged = false;

        public PullService(Notify notifyFunction)
        {
            Notify = notifyFunction;
            Load();
        }

        public void Load()
        {
            if (Config.IfPull)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }

        public void Start()
        {
            if (!switchOn)
            {
                Thread pullThread = new Thread(PullLoop);
                pullThread.SetApartmentState(ApartmentState.STA);
                pullThread.Start();
                switchOn = true;
                Program.ClipboardListener.AddHandler(ClipboardChangedHandler);
            }
        }

        public void Stop()
        {
            if (switchOn)
            {
                switchOn = false;
                Program.ClipboardListener.RemoveHandler(ClipboardChangedHandler);
            }
        }

        private void ClipboardChangedHandler()
        {
            clipboardChanged = true;
        }

        private void PullLoop()
        {
            int errorTimes = 0;
            while (switchOn)
            {
                RemoteClipboardLocker.Lock();
                String strReply = "";
                clipboardChanged = false;
                try
                {
                    Console.WriteLine("pull start " + DateTime.Now.ToString());
                    strReply = HttpWebResponseUtility.GetText(Config.GetProfileUrl(), Config.TimeOut, Config.GetHttpAuthHeader());
                    errorTimes = 0;
                    Console.WriteLine("pull end " + DateTime.Now.ToString());
                }
                catch (Exception ex)
                {
                    errorTimes += 1;
                    Console.WriteLine(ex.ToString());
                    Notify(false, true, ex.Message.ToString(), Config.GetProfileUrl() + "\n重试次数:" + errorTimes.ToString(), "重试次数:" + errorTimes.ToString(), "erro");
                    if (errorTimes > Config.RetryTimes)
                    {
                        Config.IfPull = false;
                        Config.Save();
                        Notify(true, false, "剪切板同步失败，已达到最大重试次数", ex.Message.ToString(), null, "erro");
                    }
                    continue;
                }
                finally
                {
                    RemoteClipboardLocker.Unlock();
                    Thread.Sleep((int)Config.IntervalTime);
                }

                errorTimes = 0;
                Profile remoteProfile = new Profile(strReply);
                if (!clipboardChanged)
                {
                    Profile localProfile = Profile.CreateFromLocalClipboard();
                    if (remoteProfile != localProfile)
                    {
                        remoteProfile.SetLocalClipboard();
                        Notify(true, false, "剪切板同步成功", remoteProfile.Text, null, "info");
                    }
                }
                Notify(false, true, "服务器连接成功", null, "正在同步", "info");
            }
        }
    }
}
