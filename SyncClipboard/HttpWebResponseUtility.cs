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
        private static CookieCollection savedCookies = null;

        private static void SaveCookies(CookieCollection cookies)
        {
            //savedCookies = cookies;
        }

        public static HttpWebResponse CreateGetHttpResponse(string url, int? timeout, string headerAuthorization, bool useCookies)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentNullException("url");
            }

            SetSecurityProtocol(url);
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;

            request.Method = "GET";
            request.UserAgent = DefaultUserAgent;

            if (timeout.HasValue)
            {
                request.Timeout = timeout.Value;
            }
            request.CookieContainer = new CookieContainer();
            if (useCookies && savedCookies != null)
            {
                request.CookieContainer.Add(savedCookies);
            }
            if (headerAuthorization != null)
            {
                request.Headers.Add(headerAuthorization);
            }
            return AnalyseHttpResponse((HttpWebResponse)request.GetResponse()) as HttpWebResponse;
        }

        public static HttpWebResponse CreatePutHttpResponse(string url, string parameters, int? timeout, string headerAuthorization, bool useCookies)
        {
            HttpWebRequest request = CreateHttpRequest(url, "PUT", headerAuthorization, useCookies);
            request.ContentType = "text/plain";
            request.UserAgent = DefaultUserAgent;

            if (timeout.HasValue)
            {
                request.Timeout = timeout.Value;
            }

            request.ContentType = "text/plain";

            byte[] postBytes = Encoding.UTF8.GetBytes(parameters);
            request.ContentLength = Encoding.UTF8.GetBytes(parameters).Length;
            using (Stream reqStream = request.GetRequestStream())
            {
                reqStream.Write(postBytes, 0, postBytes.Length);
            }

            return AnalyseHttpResponse((HttpWebResponse)request.GetResponse());
        }

        public static HttpWebRequest CreateHttpRequest(string url, string httpMethod, string auth, bool useCookies)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentNullException("url");
            }

            SetSecurityProtocol(url);
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
            request.Headers.Add(auth);
            request.Method = httpMethod;
            request.CookieContainer = new CookieContainer();
            if (useCookies && savedCookies != null)
            {
                request.CookieContainer.Add(savedCookies);
            }
            return request;
        }

        public static HttpWebResponse SentImageHttpContent(ref HttpWebRequest request, Image image)
        {
            request.ContentType = "application/x-bmp";

            MemoryStream mstream = new MemoryStream();
            image.Save(mstream, System.Drawing.Imaging.ImageFormat.Bmp);
            byte[] byteData = new Byte[mstream.Length];
    
            mstream.Position = 0;
            mstream.Read(byteData, 0, byteData.Length);
            mstream.Close();

            Stream reqStream = request.GetRequestStream();
            reqStream.Write(byteData, 0, byteData.Length);
            reqStream.Close();

            return AnalyseHttpResponse((HttpWebResponse)request.GetResponse());
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
            HttpStatusCode code = response.StatusCode;
            string codeMessage = response.StatusDescription;
            if (response.StatusCode < System.Net.HttpStatusCode.OK || response.StatusCode >= System.Net.HttpStatusCode.Ambiguous)
            {
                SaveCookies(new CookieCollection());
                response.Close();
                throw new WebException(response.StatusCode.GetHashCode().ToString());
            }
            SaveCookies(response.Cookies);
            return response;
        }
    } 
}
