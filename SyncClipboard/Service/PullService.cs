using System;
using System.Threading;
using SyncClipboard.Control;
using SyncClipboard.Service;
using SyncClipboard.Utility;

namespace SyncClipboard
{
    public class PullService
    {
        private const string SERVICE_NAME = "⬇⬇";

        private readonly Notifyer _notifyer;

        private readonly Notify Notify;
        private bool switchOn = false;
        private bool isChangingRemote = false;
        private PushService _pushService;

        public PullService(Notify notifyFunction)
        {
            Notify = notifyFunction;
            Load();
        }

        public PullService(Notify notifyFunction, PushService pushService, Notifyer notifyer)
        {
            pushService.PushStarted += new PushService.PushStatusChangingHandler(PushStartedHandler);
            pushService.PushStopped += new PushService.PushStatusChangingHandler(PushStoppedHandler);
            Notify = notifyFunction;
            _pushService = pushService;
            _notifyer = notifyer;
            _notifyer.SetStatusString(SERVICE_NAME, "Idle.");
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
        }

        public void PushStoppedHandler()
        {
            isChangingRemote = false;
        }

        private void PullLoop()
        {
            int errorTimes = 0;
            for (; switchOn; Thread.Sleep((int)Config.IntervalTime))
            {
                RemoteClipboardLocker.Lock();
                _notifyer.SetStatusString(SERVICE_NAME, "Reading remote profile.");

                Profile remoteProfile = null;
                try
                {
                    remoteProfile = ProfileFactory.CreateFromRemote();
                }
                catch (Exception ex)
                {
                    errorTimes += 1;
                    Log.Write(ex.ToString());
                    _notifyer.SetStatusString(SERVICE_NAME, $"Error. Failed times: {errorTimes}.", true);
                    if (errorTimes == Config.RetryTimes)
                    {
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
                _notifyer.SetStatusString(SERVICE_NAME, "Idle.", false);
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
                    SetDownloadingIcon();
                    _pushService.Stop();

                    remoteProfile.SetLocalClipboard();

                    Log.Write("剪切板同步成功:" + remoteProfile.Text);
                    Thread.Sleep(50);
                    _pushService.Start();
                    StopDownloadingIcon();
                    Notify(true, false, "剪切板同步成功", remoteProfile.ToolTip(), null, "info");
                }
            }
        }

        private void SetDownloadingIcon()
        {
            System.Drawing.Icon[] icon =
            {
                Properties.Resources.download001, Properties.Resources.download002, Properties.Resources.download003,
                Properties.Resources.download004, Properties.Resources.download005, Properties.Resources.download006,
                Properties.Resources.download007, Properties.Resources.download008, Properties.Resources.download009,
                Properties.Resources.download010, Properties.Resources.download011, Properties.Resources.download012,
                Properties.Resources.download013, Properties.Resources.download014, Properties.Resources.download015,
                Properties.Resources.download016, Properties.Resources.download017,
            };

            _notifyer.SetDynamicNotifyIcon(icon, 150);
        }

        private void StopDownloadingIcon()
        {
            _notifyer.StopDynamicNotifyIcon();
        }
    }
}
