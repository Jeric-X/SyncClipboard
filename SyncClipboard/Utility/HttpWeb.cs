using System.Drawing;
using System.Net;

namespace SyncClipboard.Utility
{
    public static class HttpWeb
    {
        public struct HttpPara
        {
            public string Url;
            public string AuthHeader;
            public CookieCollection Cookies;
        }

        public static string Post(HttpPara para, string text = null)
        {
            HttpWebRequest request = HttpWebUtility.Create(para.Url, "POST", para.AuthHeader, "application/x-www-form-urlencoded", para.Cookies);
            if (text != null)
            {
                HttpWebUtility.SentText(request, text);
            }
            return HttpWebUtility.ReceiveString(request);
        }

        public static void PutText(HttpPara para, string text)
        {
            HttpWebRequest request = HttpWebUtility.Create(para.Url, "PUT", para.AuthHeader, null, para.Cookies);
            HttpWebUtility.SentText(request, text);
            HttpWebUtility.Receive(request);
        }

        public static string GetText(HttpPara para)
        {
            HttpWebRequest request = HttpWebUtility.Create(para.Url, "GET", para.AuthHeader, null, para.Cookies);
            return HttpWebUtility.ReceiveString(request);
        }

        public static void GetFile(HttpPara para, string savePath)
        {
            HttpWebRequest request = HttpWebUtility.Create(para.Url, "GET", para.AuthHeader, null, para.Cookies);
            HttpWebUtility.ReceiveFile(request, savePath);
        }

        public static void PutImage(HttpPara para, Image image)
        {
            HttpWebRequest request = HttpWebUtility.Create(para.Url, "PUT", para.AuthHeader, null, para.Cookies);
            HttpWebUtility.SentImage(request, image);
            HttpWebUtility.Receive(request);
        }

        public static void PutFile(HttpPara para, string file)
        {
            HttpWebRequest request = HttpWebUtility.Create(para.Url, "PUT", para.AuthHeader, null, para.Cookies);
            HttpWebUtility.SentFile(request, file);
            HttpWebUtility.Receive(request);
        }

        public static string Operate(HttpPara para, string method)
        {
            HttpWebRequest request = HttpWebUtility.Create(para.Url, method, para.AuthHeader, null, para.Cookies);
            return HttpWebUtility.ReceiveString(request);
        }

        public static CookieCollection GetCookie(HttpPara para)
        {
            HttpWebRequest request = HttpWebUtility.Create(para.Url, "HEAD", para.AuthHeader, null, null);
            return HttpWebUtility.ReceiveCookie(request);
        }
    }
}
