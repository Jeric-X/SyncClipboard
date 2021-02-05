using System;
using System.Threading;
using SyncClipboard.Control;
using SyncClipboard.Utility;

namespace SyncClipboard
{
    public class PullService
    {
        private Notify Notify;
        private bool switchOn = false;
        private bool remoteClipboardChanged = true;
        private Thread pullThread = null;

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
                Thread pullThread = new Thread(DownloadClipBoard);
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
            if (remoteClipboardChanged)
            {
                remoteClipboardChanged = false;
                return;
            }

            if (pullThread != null)
            {
                Log.Write("Kill old pull thread");
                pullThread.Abort();
                pullThread = null;
            }

            pullThread = new Thread(DownloadClipBoard);
            pullThread.SetApartmentState(ApartmentState.STA);
            pullThread.Start();
        }

        private void DownloadClipBoard()
        {
            Log.Write("pull lock remote");
            try
            {
                PullLoop();
            }
            catch (ThreadAbortException ex)
            {
                Log.Write(ex.Message.ToString());
            }
            finally
            {
                Log.Write("pull unlock remote");
            }
        }

        private void PullLoop()
        {
            int errorTimes = 0;
            for (; switchOn; Thread.Sleep((int)Config.IntervalTime))
            {
                RemoteClipboardLocker.Lock();
                Log.Write("pull lock remote");
                String strReply = "";
                try
                {
                    Log.Write("pull start");
                    strReply = HttpWebResponseUtility.GetText(Config.GetProfileUrl(), Config.TimeOut, Config.GetHttpAuthHeader());
                    errorTimes = 0;
                    Log.Write("pull end");

                    Profile remoteProfile = new Profile(strReply);
                    Profile localProfile = Profile.CreateFromLocalClipboard();
                    if (remoteProfile != localProfile)
                    {
                        remoteClipboardChanged = true;
                        remoteProfile.SetLocalClipboard();
                        Notify(true, false, "剪切板同步成功", remoteProfile.Text, null, "info");
                    }
                    Notify(false, true, "服务器连接成功", null, "正在同步", "info");
                    errorTimes = 0;
                }
                catch (Exception ex)
                {
                    errorTimes += 1;
                    Log.Write(ex.ToString());
                    Notify(false, true, ex.Message.ToString(), Config.GetProfileUrl() + "\n重试次数:" + errorTimes.ToString(), "重试次数:" + errorTimes.ToString(), "erro");
                    if (errorTimes > Config.RetryTimes)
                    {
                        Config.IfPull = false;
                        Config.Save();
                        Notify(true, false, "剪切板同步失败，已达到最大重试次数", ex.Message.ToString(), null, "erro");
                    }
                }
                finally
                {
                    Log.Write("pull unlock remote");
                    RemoteClipboardLocker.Unlock();
                }
            }
        }
    }
}
