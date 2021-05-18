using System.Drawing;
using System.Net;

namespace SyncClipboard.Utility
{
    public static class HttpWeb
    {
        public static string Post(HttpPara para, string text = null)
        {
            HttpWebRequest request = HttpWebUtility.Create(para, "POST", "application/x-www-form-urlencoded");
            if (text != null)
            {
                HttpWebUtility.SentText(request, text);
            }
            return HttpWebUtility.ReceiveString(request);
        }

        public static void PutText(HttpPara para, string text)
        {
            HttpWebRequest request = HttpWebUtility.Create(para, "PUT", null);
            HttpWebUtility.SentText(request, text);
            HttpWebUtility.Receive(request);
        }

        public static string GetText(HttpPara para)
        {
            HttpWebRequest request = HttpWebUtility.Create(para, "GET", null);
            return HttpWebUtility.ReceiveString(request);
        }

        public static void GetFile(HttpPara para, string savePath)
        {
            HttpWebRequest request = HttpWebUtility.Create(para, "GET", null);
            HttpWebUtility.ReceiveFile(request, savePath);
        }

        public static void PutImage(HttpPara para, Image image)
        {
            HttpWebRequest request = HttpWebUtility.Create(para, "PUT", null);
            HttpWebUtility.SentImage(request, image);
            HttpWebUtility.Receive(request);
        }

        public static void PutFile(HttpPara para, string file)
        {
            HttpWebRequest request = HttpWebUtility.Create(para, "PUT", null);
            HttpWebUtility.SentFile(request, file);
            HttpWebUtility.Receive(request);
        }

        public static string Operate(HttpPara para, string method)
        {
            HttpWebRequest request = HttpWebUtility.Create(para, method, null);
            return HttpWebUtility.ReceiveString(request);
        }

        public static CookieCollection GetCookie(HttpPara para)
        {
            HttpWebRequest request = HttpWebUtility.Create(para, "HEAD", null);
            return HttpWebUtility.ReceiveCookie(request);
        }
    }
}
