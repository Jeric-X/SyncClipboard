using System;
using System.Threading;
using SyncClipboard.Control;
using SyncClipboard.Service;
using SyncClipboard.Utility;

namespace SyncClipboard
{
    public class PullService
    {
        private readonly Notify Notify;
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
            isChangingRemote = true;
            Log.Write("[PULL] [EVENT] push started");
        }

        public void PushStoppedHandler()
        {
            isChangingRemote = false;
            Log.Write("[PULL] [EVENT] push ended");
        }


        private void PullLoop()
        {
            int errorTimes = 0;
            for (; switchOn; Thread.Sleep((int)Config.IntervalTime))
            {
                RemoteClipboardLocker.Lock();

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

                SetRemoteProfileToLocal(remoteProfile);
                errorTimes = 0;
            }
        }

        private void SetRemoteProfileToLocal(Profile remoteProfile)
        {
            Profile localProfile = ProfileFactory.CreateFromLocal();
            if (localProfile.GetProfileType() == ProfileType.ClipboardType.Unknown)
            {
                Log.Write("[PULL] Local profile type is Unkown, stop sync.");
                return;
            }

            Log.Write("[PULL] isChangingRemote = " + isChangingRemote.ToString());
            if (!isChangingRemote && remoteProfile != localProfile)
            {
                Thread.Sleep(200);
                if (!isChangingRemote)
                {
                    remoteProfile.SetLocalClipboard();
                    Log.Write("剪切板同步成功:" + remoteProfile.Text);
                    Notify(true, false, "剪切板同步成功", remoteProfile.ToolTip(), null, "info");
                }
            }
            Notify(false, true, "服务器连接成功", null, "正在同步", "info");
        }
    }
}
