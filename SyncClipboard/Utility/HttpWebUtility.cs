using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using SyncClipboard.Module;

namespace SyncClipboard.Utility
{
    internal static class HttpWebUtility
    {
        private const string DEFAULT_USER_AGENT = "SyncClipboard";

        internal static void SentBytes(HttpWebRequest request, byte[] bytes)
        {
            request.ContentLength = bytes.Length;
            Stream reqStream = request.GetRequestStream();
            reqStream.Write(bytes, 0, bytes.Length);
            reqStream.Close();
        }

        internal static void SentText(HttpWebRequest request, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            SentBytes(request, bytes);
        }

        internal static void SentFile(HttpWebRequest request, string filePath)
        {
            Stream reqStream = request.GetRequestStream();
            FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            fs.CopyTo(reqStream);
            fs.Close();
            reqStream.Close();
        }

        internal static void SentImage(HttpWebRequest request, Image image)
        {
            MemoryStream mstream = new MemoryStream();
            image.Save(mstream, System.Drawing.Imaging.ImageFormat.Bmp);

            byte[] byteData = new byte[mstream.Length];
            mstream.Position = 0;
            mstream.Read(byteData, 0, byteData.Length);
            mstream.Close();

            SentBytes(request, byteData);
        }

        internal static string ReceiveString(HttpWebRequest request)
        {
            HttpWebResponse response = AnalyseHttpResponse((HttpWebResponse)request.GetResponse());

            StreamReader objStrmReader = new StreamReader(response.GetResponseStream());
            string receiveText = objStrmReader.ReadToEnd();

            objStrmReader.Close();
            response.Close();

            return receiveText;
        }

        internal static void ReceiveFile(HttpWebRequest request, string savePath)
        {
            HttpWebResponse response = AnalyseHttpResponse((HttpWebResponse)request.GetResponse());
            Stream respStream = response.GetResponseStream();

            FileStream fs = new FileStream(savePath, FileMode.Create);
            respStream.CopyTo(fs);
            fs.Close();
            respStream.Close();
            response.Close();
        }

        internal static CookieCollection ReceiveCookie(HttpWebRequest request)
        {
            HttpWebResponse response = AnalyseHttpResponse((HttpWebResponse)request.GetResponse());
            var cookies = response.Cookies;
            response.Close();
            return cookies;
        }

        internal static void Receive(HttpWebRequest request)
        {
            HttpWebResponse response = AnalyseHttpResponse((HttpWebResponse)request.GetResponse());
            response.Close();
        }

        internal static HttpWebRequest Create(HttpPara httpPara, string httpMethod, string contentType)
        {
            if (string.IsNullOrEmpty(httpPara.Url))
            {
                throw new ArgumentNullException(nameof(httpPara.Url));
            }

            SetSecurityProtocol(httpPara.Url);
            HttpWebRequest request = WebRequest.Create(httpPara.Url) as HttpWebRequest;
            request.Method = httpMethod;
            request.UserAgent = DEFAULT_USER_AGENT;
            request.Timeout = UserConfig.Config.Program.TimeOut;

            if (httpPara.AuthHeader != null)
            {
                request.Headers.Add(httpPara.AuthHeader);
            }

            if (!string.IsNullOrEmpty(contentType))
            {
                request.ContentType = contentType;
            }

            request.CookieContainer = new CookieContainer();
            if (httpPara.Cookies != null)
            {
                request.CookieContainer.Add(httpPara.Cookies);
            }

            return request;
        }

        private static void SetSecurityProtocol(string url)
        {
            if (url.StartsWith("https", StringComparison.OrdinalIgnoreCase))
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
        }

        internal static HttpWebResponse AnalyseHttpResponse(HttpWebResponse response)
        {
            Log.Write("HTTP RESPONSE " + response.StatusCode.ToString());
            if (response.StatusCode < HttpStatusCode.OK || response.StatusCode >= HttpStatusCode.Ambiguous)
            {
                response.Close();
                throw new WebException(response.StatusCode.ToString());
            }
            return response;
        }
    }
}
