using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities.Web
{
    public class WebDav : WebDavBase
    {
        private readonly WebDavCredential _credential;

        protected override string User => _credential.Username;
        protected override string Token => _credential.Password;
        protected override string BaseAddress => _credential.Url;

        public WebDav(WebDavCredential credential, ILogger? logger = null)
        {
            _credential = credential;
            Logger = logger;
        }
    }
}