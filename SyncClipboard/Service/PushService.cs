using SyncClipboard.Control;
using SyncClipboard.Service;
using SyncClipboard.Utility;
using System;
using System.Threading;

namespace SyncClipboard
{
    public class PushService
    {
        private readonly Notify Notify;
        private bool switchOn = false;
        private Thread pushThread = null;
        //private Profile currentProfile;

        private int pushThreadNumber = 0;
        public delegate void PushStatusChangingHandler();
        public event PushStatusChangingHandler PushStarted;
        public event PushStatusChangingHandler PushStopped;

        private void ReleasePushThreadNumber()
        {
            pushThreadNumber--;
            setPushstatusChangeEvent();
        }

        private void AddPushThreadNumber()
        {
            pushThreadNumber++;
            setPushstatusChangeEvent();
        }

        private void setPushstatusChangeEvent()
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
        }

        public PushService(Notify notifyFunction)
        {
            Notify = notifyFunction;
            Load();
        }
        
        public void Start()
        {
            if(!switchOn)
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
            if (Config.IfPush) {
                Start();
            }
            else {
                Stop();
            }
        }

        private void ClipboardChangedHandler()
        {
            if (pushThread != null)
            {
                Log.Write("Kill old push thread");
                pushThread.Abort();
                pushThread = null;
            }

            pushThread = new Thread(UploadClipBoard);
            pushThread.SetApartmentState(ApartmentState.STA);
            pushThread.Start();
            Log.Write("Create new push thread");
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
                catch(Exception ex)
                {
                    errMessage = ex.Message.ToString();
                }
                Thread.Sleep(1000);
            }
            Notify(true, false, errMessage, "未同步：" + currentProfile.ToolTip(), null, "erro");
        }

        private void UploadClipBoard()
        {
            var currentProfile = ProfileFactory.CreateFromLocal();

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

            Log.Write("[PUSH] waiting for remote profile");
            RemoteClipboardLocker.Lock();
            Log.Write("[PUSH] end waiting for remote profile");
            try
            {
                UploadLoop(currentProfile);
            }
            catch(Exception ex)
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
    }
}
