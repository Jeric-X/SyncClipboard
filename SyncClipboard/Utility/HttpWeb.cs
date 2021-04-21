using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;

namespace SyncClipboard.Utility
{
    public class HttpWeb
    {
        public static string Post(string url, string text = null, string authHeader = null)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, "POST", authHeader, "application/x-www-form-urlencoded");
            if (text != null)
            {
                HttpWebUtility.SentText(request, text);
            }
            return HttpWebUtility.ReceiveString(request);
        }

        public static void PutText(string url, string text, string authHeader = null)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, "PUT", authHeader, null);
            HttpWebUtility.SentText(request, text);
            HttpWebUtility.Receive(request);
        }

        public static string GetText(string url, string authHeader = null)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, "GET", authHeader, null);
            return HttpWebUtility.ReceiveString(request);
        }

        public static void GetFile(string url, string savePath, string authHeader)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, "GET", authHeader, null);
            HttpWebUtility.ReceiveFile(request, savePath);
        }

        public static void PutImage(string url, Image image, string authHeader)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, "PUT", authHeader, null);
            HttpWebUtility.SentImage(request, image);
            HttpWebUtility.Receive(request);
        }

        public static void PutFile(string url, string file, string authHeader)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, "PUT", authHeader, null);
            HttpWebUtility.SentFile(request, file);
            HttpWebUtility.Receive(request);
        }

        public static string Operate(string url, string method, string authHeader)
        {
            HttpWebRequest request = HttpWebUtility.Create(url, method, authHeader, null);
            return HttpWebUtility.ReceiveString(request);
        }
    }
}
