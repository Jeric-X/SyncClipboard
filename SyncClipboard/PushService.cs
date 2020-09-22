using SyncClipboard.Control;
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace SyncClipboard
{
    class PushService
    {
        private Notify Notify;
        private ClipboardListener clipboardListener;
        private bool switchOn = false;
        private Thread pushThread = null;

        public PushService(Notify notifyFunction)
        {
            Notify = notifyFunction;
            clipboardListener = new ClipboardListener();
            clipboardListener.ClipBoardChanged += ClipboardChangedHandler;
            Load();
        }
        
        public void Start()
        {
            switchOn = true;
            clipboardListener.Enable();
        }

        public void Stop()
        {
            if (switchOn)
            {
                switchOn = false;
                clipboardListener.Disable();
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

        private void UploadClipBoard()
        {
            Console.WriteLine("Push start " + DateTime.Now.ToString());
            IDataObject ClipboardData = Clipboard.GetDataObject();
            if (!ClipboardData.GetDataPresent(DataFormats.Text) && !ClipboardData.GetDataPresent(DataFormats.Bitmap))
            {
                return;
            }
            string str = Clipboard.GetText();
            Image image = Clipboard.GetImage();
            bool isImage = Clipboard.ContainsImage();

            string errMessage = "";
            for (int i = 0; i < Config.RetryTimes && switchOn; i++)
            {
                try
                {
                    if (isImage)
                    {
                        PushImage(image);
                    }
                    PushProfile(str, isImage);
                    Console.WriteLine("Push end " + DateTime.Now.ToString());
                    return;
                }
                catch(Exception ex)
                {
                    errMessage = ex.Message.ToString();
                }
                Thread.Sleep(1000);
            }
            Notify(true, false, errMessage, "未同步：" + str, null, "erro");
        }

        private void PushImage(Image image)
        {
            Console.WriteLine("sending image");
            HttpWebResponseUtility.PutImage(Config.GetImageUrl(), image, Config.TimeOut, Config.GetHttpAuthHeader());
        }

        private void PushProfile(String str, bool isImage)
        {
            Profile profile = new Profile();
            if(isImage)
            {
                profile.Type = Profile.ClipboardType.Image;
            }
            else
            {
                profile.Type = Profile.ClipboardType.Text;
                profile.Text = str;
            }

            HttpWebResponseUtility.PutText(Config.GetProfileUrl(), profile.ToJsonString(), Config.TimeOut, Config.GetHttpAuthHeader());
        }
    }
}
