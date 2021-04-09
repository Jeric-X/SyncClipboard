using SyncClipboard.Control;
using SyncClipboard.Service;
using SyncClipboard.Utility;
using System;
using System.Threading;

namespace SyncClipboard
{
    public class PushService
    {
        private const string SERVICE_NAME = "⬆⬆";

        private readonly Notifyer _notifyer;

        bool _isErrorStatus = false;
        string _statusString = "";

        private bool switchOn = false;
        private Thread pushThread = null;
        private Profile currentProfile;

        private int pushThreadNumber = 0;
        public delegate void PushStatusChangingHandler();
        public event PushStatusChangingHandler PushStarted;
        public event PushStatusChangingHandler PushStopped;

        private void ReleasePushThreadNumber()
        {
            pushThreadNumber--;
            SetPushstatusChangeEvent();
        }

        private void AddPushThreadNumber()
        {
            pushThreadNumber++;
            SetPushstatusChangeEvent();
        }

        private void SetPushstatusChangeEvent()
        {
            if (pushThreadNumber == 0)
            {
                Log.Write("[PUSH] [EVENT] push ended EVENT START");
                PushStopped?.Invoke();
            }
            else if (pushThreadNumber == 1)
            {
                Log.Write("[PUSH] [EVENT] push started EVENT START");
                PushStarted?.Invoke();
            }

            Log.Write("[PUSH] [EVENT] pushThreadNumber = " + pushThreadNumber.ToString());
        }

        public PushService(Notifyer notifyer)
        {
            _notifyer = notifyer;

            this.PushStarted += PushStartedHandler;
            this.PushStopped += PushStoppedHandler;
            _notifyer.SetStatusString(SERVICE_NAME, "Idle.");
            Load();
        }

        public void Start()
        {
            if (!switchOn)
            {
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

        public void Load()
        {
            if (Config.IfPush)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }

        private void ClipboardChangedHandler()
        {
            Mutex mutex = new Mutex();
            mutex.WaitOne();
            if (pushThread != null)
            {
                Log.Write("Kill old push thread");
                pushThread.Abort();
                pushThread = null;
            }

            currentProfile = ProfileFactory.CreateFromLocal();
            if (currentProfile == null)
            {
                return;
            }
            if (currentProfile.GetProfileType() == ProfileType.ClipboardType.Unknown)
            {
                Log.Write("[PUSH] Local profile type is Unkown, stop upload.");
                return;
            }

            AddPushThreadNumber();
            pushThread = new Thread(UploadClipBoard);
            pushThread.SetApartmentState(ApartmentState.STA);
            pushThread.Start();
            Thread.Sleep(50);
            Log.Write("Create new push thread");
            mutex.ReleaseMutex();
        }

        private void UploadLoop(Profile currentProfile)
        {
            Log.Write("Push start");

            string errMessage = "";
            for (int i = 0; i < Config.RetryTimes && switchOn; i++)
            {
                try
                {
                    currentProfile.UploadProfile();
                    Log.Write("Push end");
                    return;
                }
                catch (Exception ex)
                {
                    errMessage = ex.Message.ToString();
                    _notifyer.SetStatusString(SERVICE_NAME, $"失败，正在第{i + 1}次尝试，错误原因：{errMessage}", _isErrorStatus);
                }
                Thread.Sleep(1000);
            }
            _notifyer.ToastNotify("上传失败：" + currentProfile.ToolTip(), errMessage);
            _statusString = errMessage;
            _isErrorStatus = true;
        }

        private void UploadClipBoard()
        {
            RemoteClipboardLocker.Lock();
            try
            {
                UploadLoop(currentProfile);
            }
            catch (Exception ex)
            {
                Log.Write(ex.Message.ToString());
            }
            finally
            {
                RemoteClipboardLocker.Unlock();
                Log.Write("[PUSH] unlock remote");
                ReleasePushThreadNumber();
            }
        }

        private void SetUploadingIcon()
        {
            System.Drawing.Icon[] icon =
            {
                Properties.Resources.upload001, Properties.Resources.upload002, Properties.Resources.upload003,
                Properties.Resources.upload004, Properties.Resources.upload005, Properties.Resources.upload006,
                Properties.Resources.upload007, Properties.Resources.upload008, Properties.Resources.upload009,
                Properties.Resources.upload010, Properties.Resources.upload011, Properties.Resources.upload012,
                Properties.Resources.upload013, Properties.Resources.upload014, Properties.Resources.upload015,
                Properties.Resources.upload016, Properties.Resources.upload017,
            };

            _notifyer.SetDynamicNotifyIcon(icon, 150);
        }

        private void StopUploadingIcon()
        {
            _notifyer.StopDynamicNotifyIcon();
        }

        private void PushStartedHandler()
        {
            _statusString = "Uploading.";
            _isErrorStatus = false;
            _notifyer.SetStatusString(SERVICE_NAME, _statusString, _isErrorStatus);

            SetUploadingIcon();
        }

        private void PushStoppedHandler()
        {
            if (!_isErrorStatus)
            {
                _statusString = "Idle.";
            }
            _notifyer.SetStatusString(SERVICE_NAME, _statusString, _isErrorStatus);

            StopUploadingIcon();
        }
    }
}
