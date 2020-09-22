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

            pushThread = new Thread(UploadClipBoard);
            pushThread.SetApartmentState(ApartmentState.STA);
            pushThread.Start();
        }

        private void UploadClipBoard(Object ClipboardData)
        {
            Console.WriteLine("Push start " + DateTime.Now.ToString());
            Profile profile = Profile.CreateFromLocalClipboard();

            if (profile.Type == Profile.ClipboardType.None)
            {
                return;
            }

            string errMessage = "";
            for (int i = 0; i < Config.RetryTimes && switchOn; i++)
            {
                try
                {
                    if (profile.Type == Profile.ClipboardType.Image)
                    {
                        HttpWebResponseUtility.PutImage(Config.GetImageUrl(), profile.GetImage(), Config.TimeOut, Config.GetHttpAuthHeader());
                    }
                    HttpWebResponseUtility.PutText(Config.GetProfileUrl(), profile.ToJsonString(), Config.TimeOut, Config.GetHttpAuthHeader());
                    Console.WriteLine("Push end " + DateTime.Now.ToString());
                    return;
                }
                catch(Exception ex)
                {
                    errMessage = ex.Message.ToString();
                }
                Thread.Sleep(1000);
            }
            Notify(true, false, errMessage, "未同步：" + profile.Text, null, "erro");
        }
    }
}
