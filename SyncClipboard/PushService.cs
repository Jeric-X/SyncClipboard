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

        public PushService(Notify notifyFunction)
        {
            Notify = notifyFunction;
            clipboardListener = new ClipboardListener();
            clipboardListener.ClipBoardChanged += UploadClipBoard;
            Load();
        }
        
        public void Enable()
        {
            clipboardListener.Enable();
        }

        public void Disable()
        {
            clipboardListener.Disable();
        }

        public void Load()
        {
            if (Config.IfPush) {
                Enable();
            }
            else {
                Disable();
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
            for (int i = 0; i < Config.RetryTimes && Config.IfPush; i++)
            {
                try
                {
                    if (isImage)
                    {
                        PushImage(image);
                    }
                    PushProfile(str, isImage);
                }
                catch(Exception ex)
                {
                    errMessage = ex.Message.ToString();
                    continue;
                }

                Console.WriteLine("Push end " + DateTime.Now.ToString());
                return;
            }
            Notify(true, false, errMessage, "未同步：" + str, null, "erro");
        }

        public void PushImage(Image image)
        {
            Console.WriteLine("sending image");
            String auth = Config.GetHttpAuthHeader();
            HttpWebRequest request = HttpWebResponseUtility.CreateHttpRequest(Config.GetImageUrl(), "PUT", auth, true);
            HttpWebResponse response = HttpWebResponseUtility.SentImageHttpContent(ref request, image);
            response.Close();
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
