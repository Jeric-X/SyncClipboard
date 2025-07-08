using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.Web
{
    public class WebDavClient : WebDavBase
    {
        private SyncConfig _syncConfig;
        private ServerConfig _serverConfig;
        private readonly ConfigManager _configManager;

        protected override uint Timeout => _syncConfig.TimeOut != 0 ? _syncConfig.TimeOut : base.Timeout;
        protected override string User => _syncConfig.UseLocalServer ? _serverConfig.UserName : _syncConfig.UserName;
        protected override string Token => _syncConfig.UseLocalServer ? _serverConfig.Password : _syncConfig.Password;
        protected override string BaseAddress
        {
            get
            {
                if (_syncConfig.UseLocalServer && !_serverConfig.EnableCustomConfigurationFile)
                {
                    var protocol = _serverConfig.EnableHttps ? "https" : "http";
                    return $"{protocol}://127.0.0.1:{_serverConfig.Port}";
                }
                return _syncConfig.RemoteURL;
            }
        }

        protected override bool TrustInsecureCertificate => _syncConfig.TrustInsecureCertificate;

        private readonly IAppConfig _appConfig;
        protected override IAppConfig AppConfig => _appConfig;

        public WebDavClient(ConfigManager configManager, ILogger logger, IAppConfig appConfig)
        {
            _configManager = configManager;
            configManager.ConfigChanged += UserConfigChanged;
            _syncConfig = configManager.GetConfig<SyncConfig>();
            _serverConfig = configManager.GetConfig<ServerConfig>();
            Logger = logger;
            _appConfig = appConfig;
            ProxyManager.GlobalProxyChanged += ReInitHttpClient;
        }

        private async void UserConfigChanged()
        {
            var syncConfig = _configManager.GetConfig<SyncConfig>();
            var serverConfig = _configManager.GetConfig<ServerConfig>();

            if (_serverConfig != serverConfig || syncConfig != _syncConfig)
            {
                _serverConfig = serverConfig;
                _syncConfig = syncConfig;
                ReInitHttpClient();
                try
                {
                    await CreateDirectory(Env.RemoteFileFolder);
                }
                catch
                {
                }
            }
        }
    }
}