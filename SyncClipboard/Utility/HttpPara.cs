using System.Net;

namespace SyncClipboard.Utility
{
    public struct HttpPara
    {
        public string Url;
        public string AuthHeader;
        public CookieCollection Cookies;
    }
}