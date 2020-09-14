using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace SyncClipboard
{/// <summary>  
    /// 有关HTTP请求的辅助类  
    /// </summary>  
    public class HttpWebResponseUtility
    {
        private static readonly string DefaultUserAgent = "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; SV1; .NET CLR 1.1.4322; .NET CLR 2.0.50727)";
        /// <summary>  
        /// 创建GET方式的HTTP请求  
        /// </summary>  
        /// <param name="url">请求的URL</param>  
        /// <param name="timeout">请求的超时时间</param>  
        /// <param name="userAgent">请求的客户端浏览器信息，可以为空</param>  
        /// <param name="cookies">随同HTTP请求发送的Cookie信息，如果不需要身份验证可以为空</param>  
        /// <returns></returns>  
        public static HttpWebResponse CreateGetHttpResponse(string url, int? timeout, string userAgent, string headerAuthorization,CookieCollection cookies)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentNullException("url");
            }

            SetSecurityProtocol(url);
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;

            request.Method = "GET";
            request.UserAgent = DefaultUserAgent;
            if (!string.IsNullOrEmpty(userAgent))
            {
                request.UserAgent = userAgent;
            }
            if (timeout.HasValue)
            {
                request.Timeout = timeout.Value;
            }
            if (cookies != null)
            {
                request.CookieContainer = new CookieContainer();
                request.CookieContainer.Add(cookies);
            }
            if (headerAuthorization != null)
            {
                request.Headers.Add(headerAuthorization);
            }
            return request.GetResponse() as HttpWebResponse;
        }
        /// <summary>  
        /// 创建POST方式的HTTP请求  
        /// </summary>  
        /// <param name="url">请求的URL</param>  
        /// <param name="parameters">随同请求POST的参数名称及参数值字典</param>  
        /// <param name="timeout">请求的超时时间</param>  
        /// <param name="userAgent">请求的客户端浏览器信息，可以为空</param>  
        /// <param name="requestEncoding">发送HTTP请求时所用的编码</param>  
        /// <param name="cookies">随同HTTP请求发送的Cookie信息，如果不需要身份验证可以为空</param>  
        /// <returns></returns>  
        public static HttpWebResponse CreatePostHttpResponse(string url, IDictionary<string, string> parameters, int? timeout, string userAgent, Encoding requestEncoding, CookieCollection cookies)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentNullException("url");
            }
            if (requestEncoding == null)
            {
                throw new ArgumentNullException("requestEncoding");
            }

            SetSecurityProtocol(url);

            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;

            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";

            if (!string.IsNullOrEmpty(userAgent))
            {
                request.UserAgent = userAgent;
            }
            else
            {
                request.UserAgent = DefaultUserAgent;
            }

            if (timeout.HasValue)
            {
                request.Timeout = timeout.Value;
            }
            if (cookies != null)
            {
                request.CookieContainer = new CookieContainer();
                request.CookieContainer.Add(cookies);
            }
            //如果需要POST数据  
            if (!(parameters == null || parameters.Count == 0))
            {
                StringBuilder buffer = new StringBuilder();
                int i = 0;
                foreach (string key in parameters.Keys)
                {
                    if (i > 0)
                    {
                        buffer.AppendFormat("&{0}={1}", key, parameters[key]);
                    }
                    else
                    {
                        buffer.AppendFormat("{0}={1}", key, parameters[key]);
                    }
                    i++;
                }
                byte[] data = requestEncoding.GetBytes(buffer.ToString());
                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }
            }
            return request.GetResponse() as HttpWebResponse;
        }
        /// <summary>  
        /// 创建PUT方式的HTTP请求  
        /// </summary>  
        /// <param name="url">请求的URL</param>  
        /// <param name="parameters">随同请求PUT的参数名称</param>  
        /// <param name="timeout">请求的超时时间</param>  
        /// <param name="userAgent">请求的客户端浏览器信息，可以为空</param>  
        /// <param name="requestEncoding">发送HTTP请求时所用的编码</param>  
        /// <param name="cookies">随同HTTP请求发送的Cookie信息，如果不需要身份验证可以为空</param>  
        /// <returns></returns>  
        public static HttpWebResponse CreatePutHttpResponse(string url, string parameters, int? timeout, string userAgent, string headerAuthorization, Encoding requestEncoding, CookieCollection cookies)
        {
            
            if (requestEncoding != null)
            {
                throw new ArgumentNullException("requestEncoding");
            }
            HttpWebRequest request = CreateHttpRequest(url, "PUT", headerAuthorization);
            request.ContentType = "text/plain";
            if (!string.IsNullOrEmpty(userAgent))
            {
                request.UserAgent = userAgent;
            }
            else
            {
                request.UserAgent = DefaultUserAgent;
            }

            if (timeout.HasValue)
            {
                request.Timeout = timeout.Value;
            }
            if (cookies != null)
            {
                request.CookieContainer = new CookieContainer();
                request.CookieContainer.Add(cookies);
            }

            //如果需要POST数据  
            //request.ContentLength = parameters.Length;
            request.ContentType = "text/plain";

            byte[] postBytes = Encoding.UTF8.GetBytes(parameters);
            request.ContentLength = Encoding.UTF8.GetBytes(parameters).Length;
            using (Stream reqStream = request.GetRequestStream())
            {
                reqStream.Write(postBytes, 0, postBytes.Length);
            }

            //using (StreamWriter writer = new StreamWriter(request.GetRequestStream()))
            //{
              //  writer.Write(parameters);
           // }

            return request.GetResponse() as HttpWebResponse;
        }

        public static HttpWebRequest CreateHttpRequest(string url, string httpMethod, string auth)
        {
            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentNullException("url");
            }

            SetSecurityProtocol(url);
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
            request.Headers.Add(auth);
            request.Method = httpMethod;
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

            using (Stream reqStream = request.GetRequestStream())
            {
                reqStream.Write(byteData, 0, byteData.Length);
            }

            return request.GetResponse() as HttpWebResponse;
        }

        public static void SetSecurityProtocol(string url)
        {
            if (url.StartsWith("https", StringComparison.OrdinalIgnoreCase))
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
        }
    } 
}
