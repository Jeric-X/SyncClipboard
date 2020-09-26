using SyncClipboard.Control;
using System;
using System.Threading;

namespace SyncClipboard
{
    class PushService
    {
        private Notify Notify;
        private bool switchOn = false;
        private Thread pushThread = null;
        private Profile currentProfile;

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
                pushThread.Abort();
                pushThread = null;
            }

            currentProfile = Profile.CreateFromLocalClipboard();
            pushThread = new Thread(UploadClipBoard);
            pushThread.SetApartmentState(ApartmentState.STA);
            pushThread.Start();
        }

        private void UploadLoop()
        {
            Console.WriteLine("Push start " + DateTime.Now.ToString());

            if (currentProfile.Type == Profile.ClipboardType.None)
            {
                return;
            }

            string errMessage = "";
            for (int i = 0; i < Config.RetryTimes && switchOn; i++)
            {
                try
                {
                    if (currentProfile.Type == Profile.ClipboardType.Image)
                    {
                        HttpWebResponseUtility.PutImage(Config.GetImageUrl(), currentProfile.GetImage(), Config.TimeOut, Config.GetHttpAuthHeader());
                    }
                    HttpWebResponseUtility.PutText(Config.GetProfileUrl(), currentProfile.ToJsonString(), Config.TimeOut, Config.GetHttpAuthHeader());
                    Console.WriteLine("Push end " + DateTime.Now.ToString());
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

            RemoteClipboardLocker.Lock();
            try
            {
                UploadLoop();
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message.ToString());
            }
            finally
            {
                RemoteClipboardLocker.Unlock();
            }
        }
    }
}
