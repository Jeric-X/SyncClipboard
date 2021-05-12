using System;
using System.Threading;
using SyncClipboard.Control;
using SyncClipboard.Service;
using SyncClipboard.Utility;
using SyncClipboard.Module;

namespace SyncClipboard
{
    public class PullService
    {
        private const string SERVICE_NAME = "⬇⬇";

        private readonly Notifyer _notifyer;

        private bool switchOn = false;
        private bool isChangingRemote = false;

        public PullService(Notifyer notifyer)
        {
            // pushService.PushStarted += new PushService.PushStatusChangingHandler(PushStartedHandler);
            // pushService.PushStopped += new PushService.PushStatusChangingHandler(PushStoppedHandler);
            _notifyer = notifyer;
            _notifyer.SetStatusString(SERVICE_NAME, "Idle.");
            Load();
        }

        public void Load()
        {
            if (UserConfig.Config.SyncService.PullSwitchOn)
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
            for (; switchOn; Thread.Sleep((int)UserConfig.Config.Program.IntervalTime))
            {
                RemoteClipboardLocker.Lock();
                _notifyer.SetStatusString(SERVICE_NAME, "Reading remote profile.");

                Profile remoteProfile = null;
                try
                {
                    remoteProfile = ProfileFactory.CreateFromRemote(Program.webDav);
                }
                catch (Exception ex)
                {
                    errorTimes++;
                    Log.Write(ex.ToString());
                    _notifyer.SetStatusString(SERVICE_NAME, $"Error. Failed times: {errorTimes}.", true);
                    if (errorTimes == UserConfig.Config.Program.RetryTimes)
                    {
                        _notifyer.ToastNotify("剪切板同步失败", ex.Message);
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
                    //_pushService.Stop();

                    remoteProfile.SetLocalClipboard();

                    Log.Write("剪切板同步成功:" + remoteProfile.Text);
                    _notifyer.ToastNotify("剪切板同步成功", remoteProfile.ToolTip(), remoteProfile.ExecuteProfile());
                    StopDownloadingIcon();

                    Thread.Sleep(50);
                    //_pushService.Start();
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
