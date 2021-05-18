using System.Drawing;
using System.Net;

namespace SyncClipboard.Utility
{
    public static class HttpWeb
    {
        public static string Post(string url, HttpPara para, string text = null)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, para, "POST", "application/x-www-form-urlencoded");
            if (text != null)
            {
                HttpWebUtility.SentText(request, text);
            }
            return HttpWebUtility.ReceiveString(request);
        }

        public static void PutText(string url, HttpPara para, string text)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, para, "PUT", null);
            HttpWebUtility.SentText(request, text);
            HttpWebUtility.Receive(request);
        }

        public static string GetText(string url, HttpPara para)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, para, "GET", null);
            return HttpWebUtility.ReceiveString(request);
        }

        public static void GetFile(string url, HttpPara para, string savePath)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, para, "GET", null);
            HttpWebUtility.ReceiveFile(request, savePath);
        }

        public static void PutImage(string url, HttpPara para, Image image)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, para, "PUT", null);
            HttpWebUtility.SentImage(request, image);
            HttpWebUtility.Receive(request);
        }

        public static void PutFile(string url, HttpPara para, string file)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, para, "PUT", null);
            HttpWebUtility.SentFile(request, file);
            HttpWebUtility.Receive(request);
        }

        public static string Operate(string url, HttpPara para, string method)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, para, method, null);
            return HttpWebUtility.ReceiveString(request);
        }

        public static CookieCollection GetCookie(string url, HttpPara para)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, para, "HEAD", null);
            return HttpWebUtility.ReceiveCookie(request);
        }
    }
}
