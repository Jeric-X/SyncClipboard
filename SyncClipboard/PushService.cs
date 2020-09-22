using SyncClipboard.Control;
using System;
using System.Drawing;
using System.Net;
using System.Windows.Forms;

namespace SyncClipboard
{
    class PushService
    {
        Notify Notify;
        ClipboardListener clipboardListener;
        private bool switchOn = false;

        public PushService(Notify notifyFunction)
        {
            Notify = notifyFunction;
            clipboardListener = new ClipboardListener();
            clipboardListener.ClipBoardChanged += UploadClipBoard;
            Load();
        }
        
        public void Start()
        {
            switchOn = true;
            clipboardListener.Enable();
        }

        public void Stop()
        {
            switchOn = false;
            clipboardListener.Disable();
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

        void UploadClipBoard()
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
                System.Threading.Thread.Sleep(1000);
            }
            Notify(true, false, errMessage, "未同步：" + str, null, "erro");
        }

        public void PushImage(Image image)
        {
            Console.WriteLine("sending image");
            HttpWebResponseUtility.PutImage(Config.GetImageUrl(), image, Config.TimeOut, Config.GetHttpAuthHeader());
        }

        public void PushProfile(String str, bool isImage)
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

            String url = Config.GetProfileUrl();
            String auth = Config.GetHttpAuthHeader();

            HttpWebResponse response = HttpWebResponseUtility.CreatePutHttpResponse(url, profile.ToJsonString(), Config.TimeOut, auth, true);
            response.Close();
        }

        private void HandleHttpException(Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}
