using System;
using System.Drawing;
using System.Net;

namespace SyncClipboard
{
    class PushService
    {
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

        private void HandleHttpException(Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}
