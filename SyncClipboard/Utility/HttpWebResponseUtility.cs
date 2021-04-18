using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;

namespace SyncClipboard
{
    public class HttpWebResponseUtility
    {
        private static readonly string DefaultUserAgent = "SyncClipboard";
        //private static CookieCollection savedCookies = null;

        private static void SaveCookies(CookieCollection cookies)
        {
            //savedCookies = cookies;
        }

        public static string PostText(string url, string text, string authHeader = null)
        {
            string contenType = "application/x-www-form-urlencoded";
            return OperateText(url, "POST", text, authHeader, contenType);
        }

        public static string Post(string url, string authHeader = null)
        {
            HttpWebRequest request = CreateHttpRequest(url, "POST", authHeader, null);
            return GetString(request);
        }

        public static string Operate(string url, string method, string authHeader)
        {
            HttpWebRequest request = CreateHttpRequest(url, method, authHeader, null);
            return GetString(request);
        }

        public static string GetText(string url, string authHeader = null)
        {
            HttpWebRequest request = CreateHttpRequest(url, "GET", authHeader, null);
            HttpWebResponse response = AnalyseHttpResponse((HttpWebResponse)request.GetResponse());

            StreamReader objStrmReader = new StreamReader(response.GetResponseStream());
            string text = objStrmReader.ReadToEnd();

            objStrmReader.Close();
            response.Close();

            return text;
        }

        public static void Getfile(string url, string savePath, string authHeader)
        {
            HttpWebRequest request = CreateHttpRequest(url, "GET", authHeader, null);
            HttpWebResponse response = AnalyseHttpResponse((HttpWebResponse)request.GetResponse());
            Stream respStream = response.GetResponseStream();

            FileStream fs = new FileStream(savePath, FileMode.Create);
            respStream.CopyTo(fs);
            fs.Close();
            respStream.Close();
            response.Close();
        }

        public static void PutText(string url, string text, string authHeader)
        {
            OperateText(url, "PUT", text, authHeader, null);
        }

        private static string OperateText(string url, string method, string text, string authHeader, string contentType)
        {
            byte[] postBytes = Encoding.UTF8.GetBytes(text);
            return OperateByte(url, postBytes, method, authHeader, contentType);
        }

        public static HttpWebRequest CreateHttpRequest(string url, string httpMethod, string authHeader, string contentType)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentNullException("url");
            }

            SetSecurityProtocol(url);
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
            request.Method = httpMethod;
            request.UserAgent = DefaultUserAgent;
            request.Timeout = UserConfig.Config.Program.TimeOut;

            if (authHeader != null)
            {
                request.Headers.Add(authHeader);
            }

            if (!string.IsNullOrEmpty(contentType))
            {
                request.ContentType = contentType;
            }

            request.CookieContainer = new CookieContainer();
            // if (useCookies && savedCookies != null)
            // {
            //     request.CookieContainer.Add(savedCookies);
            // }
            return request;
        }

        public static void PutImage(string url, Image image, string authHeader)
        {
            MemoryStream mstream = new MemoryStream();
            image.Save(mstream, System.Drawing.Imaging.ImageFormat.Bmp);
            byte[] byteData = new Byte[mstream.Length];

            mstream.Position = 0;
            mstream.Read(byteData, 0, byteData.Length);
            mstream.Close();

            OperateByte(url, byteData, "PUT", authHeader, null);
        }

        public static void PutFile(string url, string file, string authHeader)
        {
            HttpWebRequest request = CreateHttpRequest(url, "PUT", authHeader, null);
            Stream reqStream = request.GetRequestStream();

            FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read);
            fs.CopyTo(reqStream);
            fs.Close();
            reqStream.Close();

            HttpWebResponse response = AnalyseHttpResponse((HttpWebResponse)request.GetResponse());
            response.Close();
        }

        private static string GetString(HttpWebRequest request)
        {
            HttpWebResponse response = AnalyseHttpResponse((HttpWebResponse)request.GetResponse());

            StreamReader objStrmReader = new StreamReader(response.GetResponseStream());
            string receiveText = objStrmReader.ReadToEnd();

            objStrmReader.Close();
            response.Close();

            return receiveText;
        }
        private static string OperateByte(string url, byte[] byteData, string method, string authHeader, string contentType)
        {
            HttpWebRequest request = CreateHttpRequest(url, method, authHeader, contentType);
            request.ContentLength = byteData.Length;
            Stream reqStream = request.GetRequestStream();
            reqStream.Write(byteData, 0, byteData.Length);
            reqStream.Close();

            return GetString(request);
        }

        public static void SetSecurityProtocol(string url)
        {
            if (url.StartsWith("https", StringComparison.OrdinalIgnoreCase))
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
        }

        private static HttpWebResponse AnalyseHttpResponse(HttpWebResponse response)
        {
            Utility.Log.Write("HTTP RESPONSE " + response.StatusCode.ToString());
            if (response.StatusCode < System.Net.HttpStatusCode.OK || response.StatusCode >= System.Net.HttpStatusCode.Ambiguous)
            {
                SaveCookies(new CookieCollection());
                response.Close();
                throw new WebException(response.StatusCode.ToString());
            }
            SaveCookies(response.Cookies);
            return response;
        }


        public static string GetMD5Hash(byte[] bytedata)
        {
            try
            {
                System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
                byte[] retVal = md5.ComputeHash(bytedata);

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < retVal.Length; i++)
                {
                    sb.Append(retVal[i].ToString("x2"));
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                throw new Exception("GetMD5Hash() fail,error:" + ex.Message);
            }
        }
    }
}
