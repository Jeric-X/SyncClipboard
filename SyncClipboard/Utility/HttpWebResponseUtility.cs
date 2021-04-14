using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;

namespace SyncClipboard
{
    public class HttpWebResponseUtility
    {
        private static readonly string DefaultUserAgent = "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; SV1; .NET CLR 1.1.4322; .NET CLR 2.0.50727)";
        //private static CookieCollection savedCookies = null;

        private static void SaveCookies(CookieCollection cookies)
        {
            //savedCookies = cookies;
        }

        public static string GetText(string url, string authHeader)
        {
            HttpWebRequest request = CreateHttpRequest(url, "GET", authHeader);
            HttpWebResponse response = AnalyseHttpResponse((HttpWebResponse)request.GetResponse());

            StreamReader objStrmReader = new StreamReader(response.GetResponseStream());
            string text = objStrmReader.ReadToEnd();

            objStrmReader.Close();
            response.Close();

            return text;
        }

        public static void Getfile(string url, string savePath, string authHeader)
        {
            HttpWebRequest request = CreateHttpRequest(url, "GET", authHeader);
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
            byte[] postBytes = Encoding.UTF8.GetBytes(text);
            PutByte(url, postBytes, authHeader);
        }

        public static HttpWebRequest CreateHttpRequest(string url, string httpMethod, string authHeader)
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

            PutByte(url, byteData, authHeader);
        }

        public static void PutFile(string url, string file, string authHeader)
        {
            HttpWebRequest request = CreateHttpRequest(url, "PUT", authHeader);
            Stream reqStream = request.GetRequestStream();

            FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read);
            fs.CopyTo(reqStream);
            fs.Close();
            reqStream.Close();

            HttpWebResponse response = AnalyseHttpResponse((HttpWebResponse)request.GetResponse());
            response.Close();
        }

        private static void PutByte(string url, byte[] byteData, string authHeader)
        {
            HttpWebRequest request = CreateHttpRequest(url, "PUT", authHeader);

            Stream reqStream = request.GetRequestStream();
            reqStream.Write(byteData, 0, byteData.Length);
            reqStream.Close();

            HttpWebResponse response = AnalyseHttpResponse((HttpWebResponse)request.GetResponse());
            response.Close();
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
