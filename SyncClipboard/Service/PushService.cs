using SyncClipboard.Control;
using SyncClipboard.Utility;
using System;
using System.Threading;

namespace SyncClipboard
{
    public class PushService
    {
        private Notify Notify;
        private bool switchOn = false;
        private Thread pushThread = null;
        private Profile currentProfile;

        public delegate void PushStatusChangingHandler();
        public event PushStatusChangingHandler PushStarted;
        public event PushStatusChangingHandler PushStopped;

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

            currentProfile = ProfileFactory.CreateFromLocal();
            pushThread = new Thread(UploadClipBoard);
            pushThread.SetApartmentState(ApartmentState.STA);
            pushThread.Start();
            Log.Write("Create new push thread");
        }

        private void UploadLoop()
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
            Notify(true, false, errMessage, "未同步：" + currentProfile.Text, null, "erro");
        }

        private void UploadClipBoard()
        {
            if (currentProfile == null)
            {
                return;
            }

            PushStarted.Invoke();
            Log.Write("push lock remote");
            RemoteClipboardLocker.Lock();
            try
            {
                UploadLoop();
            }
            catch(Exception ex)
            {
                Log.Write(ex.Message.ToString());
            }
            finally
            {
                Log.Write("push unlock remote");
                RemoteClipboardLocker.Unlock();
                PushStopped.Invoke();
            }
        }
    }
}
