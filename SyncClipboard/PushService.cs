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
            try
            {
                HttpWebRequest request = HttpWebResponseUtility.CreateHttpRequest(Config.GetImageUrl(), "PUT", auth);
                HttpWebResponse response = HttpWebResponseUtility.SentImageHttpContent(ref request, image);
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }

            return true;
        }
    }
}
