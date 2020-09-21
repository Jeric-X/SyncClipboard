using SyncClipboard.Control;
using System;
using System.Drawing;
using System.Net;

namespace SyncClipboard
{
    class PushService
    {
        Notify Notify;

        public PushService(Notify notifyFunction)
        {
            Notify = notifyFunction;
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
