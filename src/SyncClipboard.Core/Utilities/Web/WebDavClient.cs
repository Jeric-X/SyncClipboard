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
        protected override string BaseAddress => _syncConfig.UseLocalServer ? $"http://127.0.0.1:{_serverConfig.Port}" : _syncConfig.RemoteURL;

        public WebDavClient(ConfigManager configManager, ILogger logger)
        {
            _configManager = configManager;
            configManager.ConfigChanged += UserConfigChanged;
            _syncConfig = configManager.GetConfig<SyncConfig>(ConfigKey.Sync) ?? new();
            _serverConfig = configManager.GetConfig<ServerConfig>(ConfigKey.Server) ?? new();
            Logger = logger;
        }

        private async void UserConfigChanged()
        {
            var syncConfig = _configManager.GetConfig<SyncConfig>(ConfigKey.Sync) ?? new();
            var serverConfig = _configManager.GetConfig<ServerConfig>(ConfigKey.Server) ?? new();

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