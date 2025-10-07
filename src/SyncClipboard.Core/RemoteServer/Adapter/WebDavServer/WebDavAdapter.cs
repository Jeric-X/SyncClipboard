using SyncClipboard.Abstract;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Web;
using System.Net;

namespace SyncClipboard.Core.RemoteServer.Adapter.WebDavServer;

public sealed class WebDavAdapter : IServerAdapter<WebDavConfig>, IStorageBasedServerAdapter, IDisposable
{
    #region constants
    private const string RemoteProfilePath = "SyncClipboard.json";
    private const string RemoteFileFolder = "file";
    #endregion

    private WebDav _webDav;
    private readonly ILogger _logger;
    private readonly IAppConfig _appConfig;

    private WebDavConfig _webDavConfig;
    private SyncConfig? _syncConfig;

    public WebDavAdapter(ILogger logger, IAppConfig appConfig)
    {
        _logger = logger;
        _appConfig = appConfig;

        _webDavConfig = new WebDavConfig(); // 默认配置，将通过OnConfigChanged更新
        _syncConfig = new SyncConfig(); // 默认SyncConfig，将通过OnConfigChanged更新
        _webDav = CreateWebDavInstance();
    }

    public void OnConfigChanged(WebDavConfig config, SyncConfig syncConfig)
    {
        _webDavConfig = config;
        _syncConfig = syncConfig;

        _webDav?.Dispose();
        _webDav = CreateWebDavInstance();
    }

    private WebDav CreateWebDavInstance()
    {
        var credential = new WebDavCredential
        {
            Username = _webDavConfig.UserName,
            Password = _webDavConfig.Password,
            Url = _webDavConfig.RemoteURL
        };

        var timeout = _syncConfig?.TimeOut != 0 ? _syncConfig?.TimeOut ?? 100u : 100u; // 默认100秒
        var trustInsecureCertificate = _syncConfig?.TrustInsecureCertificate ?? false;
        var webDav = new WebDav(credential, _appConfig, trustInsecureCertificate, _logger) { Timeout = timeout };

        return webDav;
    }

    public Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return _webDav.Test(cancellationToken);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _webDav.CreateDirectory(RemoteFileFolder, cancellationToken);
    }

    public async Task<ClipboardProfileDTO?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var profileDto = await _webDav.GetJson<ClipboardProfileDTO>(RemoteProfilePath, cancellationToken);
        return profileDto;
    }

    public async Task SetProfileAsync(ClipboardProfileDTO profileDto, CancellationToken cancellationToken = default)
    {
        await _webDav.PutJson(RemoteProfilePath, profileDto, cancellationToken);
    }

    public async Task UploadFileAsync(string fileName, string localPath, CancellationToken cancellationToken = default)
    {
        var remotePath = $"{RemoteFileFolder}/{fileName}";
        await _webDav.PutFile(remotePath, localPath, cancellationToken);
        _logger.Write($"[WEBDAV] Upload completed for {fileName}");
    }

    public async Task DownloadFileAsync(string fileName, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var remotePath = $"{RemoteFileFolder}/{fileName}";

        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await _webDav.GetFile(remotePath, localPath, progress, cancellationToken);
        _logger.Write($"[WEBDAV] Downloaded {fileName} to {localPath}");
    }

    public async Task CleanupTempFilesAsync(CancellationToken cancellationToken = default)
    {
        if (_webDavConfig.DeletePreviousFilesOnPush)
        {
            try
            {
                await _webDav.DirectoryDelete(RemoteFileFolder, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound)
            {
                // 如果目录不存在，直接忽略
            }

            // 重新创建目录
            await _webDav.CreateDirectory(RemoteFileFolder, cancellationToken);
        }
    }

    public void Dispose()
    {
        _webDav?.Dispose();
    }
}