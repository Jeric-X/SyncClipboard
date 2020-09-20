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

        public bool PushImage(Image image)
        {
            Console.WriteLine("sending image");
            String auth = "Authorization: Basic " + Config.Auth;
            HttpWebRequest request = HttpWebResponseUtility.CreateHttpRequest(Config.GetImageUrl(), "PUT", auth);
            HttpWebResponse response = null;
            try
            {
                response = HttpWebResponseUtility.SentImageHttpContent(ref request, image);
            }
            catch(Exception ex)
            {
                HandleHttpException(ex);
                return false;
            }
            finally
            {
                response.Close();
            }

            return true;
        }

        public HttpWebResponse PushProfile(String str, bool isImage)
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
            String auth = "Authorization: Basic " + Config.Auth;

            return HttpWebResponseUtility.CreatePutHttpResponse(url, profile.ToJsonString(), Config.TimeOut, null, auth, null, null);
        }


        private void HandleHttpException(Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}
