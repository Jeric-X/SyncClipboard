using System;
using System.Threading;
using SyncClipboard.Control;
using SyncClipboard.Service;
using SyncClipboard.Utility;

namespace SyncClipboard
{
    public class PullService
    {
        private Notify Notify;
        private bool switchOn = false;
        private bool isChangingRemote = false;

        public PullService(Notify notifyFunction)
        {
            Notify = notifyFunction;
            Load();
        }

        public PullService(Notify notifyFunction, PushService pushService)
        {
            pushService.PushStarted += new PushService.PushStatusChangingHandler(PushStartedHandler);
            pushService.PushStopped += new PushService.PushStatusChangingHandler(PushStoppedHandler);
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
            }
        }

        public void Stop()
        {
            if (switchOn)
            {
                switchOn = false;
            }
        }

        public void PushStartedHandler()
        {
            Log.Write("Push Started1");
            isChangingRemote = true;
            Log.Write("Push Started2");
        }

        public void PushStoppedHandler()
        {
            Log.Write("Push Ended1");
            isChangingRemote = false;
            Log.Write("Push Ended2");
        }


        private void PullLoop()
        {
            int errorTimes = 0;
            for (; switchOn; Thread.Sleep((int)Config.IntervalTime))
            {
                RemoteClipboardLocker.Lock();
                Log.Write("pull lock remote");
                Profile remoteProfile = null;
                try
                {
                    remoteProfile = ProfileFactory.CreateFromRemote();
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
                    continue;
                }
                finally
                {
                    Log.Write("pull unlock remote");
                    RemoteClipboardLocker.Unlock();
                }

                Profile localProfile = ProfileFactory.CreateFromLocal();
                if (!isChangingRemote && remoteProfile != localProfile)
                {
                    remoteProfile.SetLocalClipboard();
                    Log.Write("剪切板同步成功:" + remoteProfile.Text);
                    Notify(true, false, "剪切板同步成功", remoteProfile.Text, null, "info");
                }
                Notify(false, true, "服务器连接成功", null, "正在同步", "info");
                errorTimes = 0;
            }
        }
    }
}
