using SyncClipboard.Control;
using SyncClipboard.Service;
using SyncClipboard.Utility;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard
{
    public class PushService
    {
        private const string SERVICE_NAME = "⬆⬆";

        private readonly Notifyer _notifyer;

        bool _isErrorStatus = false;
        string _statusString = "";

        private bool switchOn = false;

        private CancellationTokenSource _cancelToken = new CancellationTokenSource();
        private Task _task = Task.Run(() => {/* Do Nothing */});

        private Profile _currentProfile;
        private Object _currentProfileMutex = new Object();

        private int _uploadTaskNumber = 0;
        public delegate void PushStatusChangingHandler();
        public event PushStatusChangingHandler PushStarted;
        public event PushStatusChangingHandler PushStopped;

        private void ReleasePushThreadNumber()
        {
            Interlocked.Decrement(ref _uploadTaskNumber);
            SetPushstatusChangeEvent();
        }

        private void AddPushThreadNumber()
        {
            Interlocked.Increment(ref _uploadTaskNumber);
            SetPushstatusChangeEvent();
        }

        private void SetPushstatusChangeEvent()
        {
            if (_uploadTaskNumber == 0)
            {
                Log.Write("[PUSH] [EVENT] push ended EVENT START");
                PushStopped?.Invoke();
            }
            else if (_uploadTaskNumber == 1)
            {
                Log.Write("[PUSH] [EVENT] push started EVENT START");
                PushStarted?.Invoke();
            }

            Log.Write("[PUSH] [EVENT] pushThreadNumber = " + _uploadTaskNumber.ToString());
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
            if (UserConfig.Config.SyncService.PushSwitchOn)
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
            lock (_currentProfileMutex)
            {
                _currentProfile = ProfileFactory.CreateFromLocal();
                if (_currentProfile == null)
                {
                    return;
                }
                if (_currentProfile.GetProfileType() == ProfileType.ClipboardType.Unknown)
                {
                    Log.Write("[PUSH] Local profile type is Unkown, stop upload.");
                    return;
                }
            }

            lock (_task)
            {
                _cancelToken.Cancel();
                _cancelToken = new CancellationTokenSource();

                AddPushThreadNumber();

                _task = _task.ContinueWith(
                    (Task task) =>
                    {
                        UploadLoop(_cancelToken.Token, _task);
                    },
                    _cancelToken.Token
                ).ContinueWith(
                    (Task task) =>
                    {
                        ReleasePushThreadNumber();
                    }
                );
            }
        }

        private void UploadLoop(CancellationToken token, Task iii)
        {
            Log.Write("[PUSH] start loop");
            RemoteClipboardLocker.Lock();
            string errMessage = "";
            for (int i = 0; i < UserConfig.Config.Program.RetryTimes && switchOn; i++)
            {
                if (token.IsCancellationRequested)
                {
                    RemoteClipboardLocker.Unlock();
                    return;
                }

                try
                {
                    _currentProfile.UploadProfile();
                    Log.Write("[PUSH] upload end");
                    RemoteClipboardLocker.Unlock();
                    return;
                }
                catch (Exception ex)
                {
                    errMessage = ex.Message.ToString();
                    _notifyer.SetStatusString(SERVICE_NAME, $"失败，正在第{i + 1}次尝试，错误原因：{errMessage}", _isErrorStatus);
                }

                if (token.IsCancellationRequested)
                {
                    Log.Write("DDDD" + token.IsCancellationRequested);
                    RemoteClipboardLocker.Unlock();
                    return;
                }
                Thread.Sleep(UserConfig.Config.Program.IntervalTime);
            }
            RemoteClipboardLocker.Unlock();
            _notifyer.ToastNotify("上传失败：" + _currentProfile.ToolTip(), errMessage);
            _statusString = errMessage;
            _isErrorStatus = true;
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
