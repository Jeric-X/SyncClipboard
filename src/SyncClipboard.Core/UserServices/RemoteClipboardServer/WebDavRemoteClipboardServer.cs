using SyncClipboard.Abstract;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Web;

namespace SyncClipboard.Core.UserServices.RemoteClipboardServer;

public class WebDavRemoteClipboardServer : IRemoteClipboardServer
{
    private readonly ILogger _logger;
    private readonly ILocalFileCacheManager _cacheManager;
    private readonly ConfigManager _configManager;
    private readonly IAppConfig _appConfig;
    
    // 内部WebDav实例
    private WebDav _webDav;
    
    // 轮询相关
    private Timer? _pollingTimer;
    private ClipboardProfileDTO? _lastKnownProfile;
    private readonly object _pollingLock = new object();
    private bool _isPolling = false;
    
    // 配置缓存
    private SyncConfig _syncConfig;
    private ServerConfig _serverConfig;
    
    // 路径配置
    private readonly string _remoteProfilePath;
    private readonly string _remoteBaseFolder;
    private readonly string _remoteFilesFolder;
    private readonly string _remoteGroupsFolder;
    private readonly string _remoteOthersFolder;

    public event EventHandler<ProfileChangedEventArgs>? RemoteProfileChanged;

    public WebDavRemoteClipboardServer(ILogger logger, ILocalFileCacheManager cacheManager, ConfigManager configManager, IAppConfig appConfig)
    {
        _logger = logger;
        _cacheManager = cacheManager;
        _configManager = configManager;
        _appConfig = appConfig;
        
        // 获取配置
        _syncConfig = configManager.GetConfig<SyncConfig>();
        _serverConfig = configManager.GetConfig<ServerConfig>();
        
        // 创建内部WebDav实例
        _webDav = CreateWebDavInstance();
        
        // 监听配置变化
        configManager.ConfigChanged += OnConfigChanged;
        
        // 初始化路径配置
        _remoteProfilePath = "SyncClipboard/profile.json";
        _remoteBaseFolder = "SyncClipboard";
        _remoteFilesFolder = "SyncClipboard/Files";
        _remoteGroupsFolder = "SyncClipboard/Groups";
        _remoteOthersFolder = "SyncClipboard/Others";
    }

    private void OnConfigChanged()
    {
        // 重新获取配置
        _syncConfig = _configManager.GetConfig<SyncConfig>();
        _serverConfig = _configManager.GetConfig<ServerConfig>();
        
        // 重新创建WebDav实例
        _webDav?.Dispose();
        _webDav = CreateWebDavInstance();
    }

    private WebDav CreateWebDavInstance()
    {
        var credential = new WebDavCredential
        {
            Username = _syncConfig.UseLocalServer ? _serverConfig.UserName : _syncConfig.UserName,
            Password = _syncConfig.UseLocalServer ? _serverConfig.Password : _syncConfig.Password,
            Url = GetBaseAddress()
        };

        var timeout = _syncConfig.TimeOut != 0 ? _syncConfig.TimeOut : 30000; // 默认30秒
        var webDav = new WebDav(credential, _appConfig, _syncConfig.TrustInsecureCertificate, _logger);
        
        return webDav;
    }

    private string GetBaseAddress()
    {
        if (_syncConfig.UseLocalServer && !_serverConfig.EnableCustomConfigurationFile)
        {
            var protocol = _serverConfig.EnableHttps ? "https" : "http";
            return $"{protocol}://127.0.0.1:{_serverConfig.Port}";
        }
        return _syncConfig.RemoteURL;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // 初始化连接，创建必要的远程目录等
        try
        {
            await _webDav.CreateDirectory(_remoteBaseFolder, cancellationToken);
            await _webDav.CreateDirectory(_remoteFilesFolder, cancellationToken);
            await _webDav.CreateDirectory(_remoteGroupsFolder, cancellationToken);
            await _webDav.CreateDirectory(_remoteOthersFolder, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Write($"Warning: Failed to create remote directories: {ex.Message}");
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _webDav.Exist(_remoteBaseFolder, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task<ClipboardProfileDTO?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var profileDto = await _webDav.GetJson<ClipboardProfileDTO>(_remoteProfilePath, cancellationToken);
            return profileDto;
        }
        catch (Exception ex)
        {
            _logger.Write($"Failed to get profile from remote: {ex.Message}");
            return null;
        }
    }

    public async Task SetProfileAsync(ClipboardProfileDTO profile, CancellationToken cancellationToken = default)
    {
        var oldProfile = await GetProfileAsync(cancellationToken);
        
        await _webDav.PutJson(_remoteProfilePath, profile, cancellationToken);
        
        // 更新本地已知的Profile
        _lastKnownProfile = profile;
        
        _logger.Write($"[PUSH] Profile metadata updated: {System.Text.Json.JsonSerializer.Serialize(profile)}");
    }

    public async Task<Profile> SetBlankProfileAsync(CancellationToken cancellationToken = default)
    {
        var blankProfile = new TextProfile("");
        await SetProfileAsync(blankProfile.ToDto(), cancellationToken);
        return blankProfile;
    }

    public async Task UploadProfileDataAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        if (!profile.HasDataFile)
        {
            return; // 没有数据文件需要上传
        }

        // 如果需要准备数据（如GroupProfile的压缩），先准备
        if (profile.RequiresPrepareData)
        {
            await profile.PrepareDataAsync(cancellationToken);
        }

        try
        {
            var localDataPath = profile.GetLocalDataPath();
            if (string.IsNullOrEmpty(localDataPath) || !File.Exists(localDataPath))
            {
                throw new FileNotFoundException($"Local data file not found: {localDataPath}");
            }

            var remotePath = $"{GetRemoteFileFolder(profile)}/{profile.FileName}";
            
            using var stream = await profile.GetDataStreamAsync();
            await _webDav.PutFile(remotePath, localDataPath, cancellationToken);
            
            _logger.Write($"[PUSH] Upload completed for {profile.FileName}");
        }
        finally
        {
            // 清理准备的数据
            if (profile.RequiresPrepareData)
            {
                await profile.CleanupPreparedDataAsync();
            }
        }
    }

    public async Task DownloadProfileDataAsync(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!profile.HasDataFile)
        {
            return; // 没有数据文件需要下载
        }

        var remotePath = $"{GetRemoteFileFolder(profile)}/{profile.FileName}";
        
        // 检查缓存
        if (profile is FileProfile fileProfile && !string.IsNullOrEmpty(fileProfile.Hash))
        {
            var cachedPath = await _cacheManager.GetCachedFilePathAsync("FileProfile", fileProfile.Hash);
            if (!string.IsNullOrEmpty(cachedPath) && File.Exists(cachedPath))
            {
                _logger.Write($"[PULL] Using cached file for {profile.FileName}");
                profile.SetLocalDataPath(cachedPath);
                return;
            }
        }

        // 从远程下载到临时文件
        var tempPath = Path.GetTempFileName();
        try
        {
            await _webDav.GetFile(remotePath, tempPath, progress, cancellationToken);
            
            // 如果是文件类型且有hash，保存到缓存
            if (profile is FileProfile fileProfileForCache && !string.IsNullOrEmpty(fileProfileForCache.Hash))
            {
                await _cacheManager.SaveCacheEntryAsync("FileProfile", fileProfileForCache.Hash, tempPath);
                var cachedPath = await _cacheManager.GetCachedFilePathAsync("FileProfile", fileProfileForCache.Hash);

                if (!string.IsNullOrEmpty(cachedPath))
                {
                    profile.SetLocalDataPath(cachedPath);
                    if (tempPath != cachedPath && File.Exists(tempPath))
                    {
                        File.Delete(tempPath); // 删除临时文件
                    }
                    return;
                }
            }

            // 设置临时文件路径
            profile.SetLocalDataPath(tempPath);
            _logger.Write($"[PULL] Downloaded {profile.FileName} to {tempPath}");
        }
        catch
        {
            // 清理临时文件
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            throw;
        }
    }

    public async Task DeleteProfileDataAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        if (!profile.HasDataFile)
        {
            return;
        }

        var remotePath = $"{GetRemoteFileFolder(profile)}/{profile.FileName}";
        await _webDav.Delete(remotePath, cancellationToken);
        _logger.Write($"[DELETE] Removed remote file {profile.FileName}");
    }

    private string GetRemoteFileFolder(Profile profile)
    {
        // 根据Profile类型确定远程文件夹路径
        return profile.Type switch
        {
            ProfileType.File => _remoteFilesFolder,
            ProfileType.Group => _remoteGroupsFolder, 
            _ => _remoteOthersFolder
        };
    }

    public async Task StartPollingAsync(CancellationToken cancellationToken = default)
    {
        lock (_pollingLock)
        {
            if (_isPolling) return;
            _isPolling = true;
        }

        // 获取当前远程Profile作为基准
        _lastKnownProfile = await GetProfileAsync(cancellationToken);
        
        // 设置轮询间隔（默认5秒）
        var pollingInterval = TimeSpan.FromSeconds(5);
        
        _pollingTimer = new Timer(async _ => await CheckRemoteProfileChanges(), null, pollingInterval, pollingInterval);
        
        _logger.Write("[POLLING] Started monitoring remote profile changes");
    }

    public void StopPolling()
    {
        lock (_pollingLock)
        {
            if (!_isPolling) return;
            _isPolling = false;
        }

        _pollingTimer?.Dispose();
        _pollingTimer = null;
        
        _logger.Write("[POLLING] Stopped monitoring remote profile changes");
    }

    private async Task CheckRemoteProfileChanges()
    {
        try
        {
            var currentProfile = await GetProfileAsync();
            
            // 比较Profile是否有变化
            if (HasProfileChanged(_lastKnownProfile, currentProfile))
            {
                var oldProfile = _lastKnownProfile;
                _lastKnownProfile = currentProfile;
                
                // 触发远程Profile变更事件
                RemoteProfileChanged?.Invoke(this, new ProfileChangedEventArgs 
                { 
                    NewProfile = currentProfile, 
                    OldProfile = oldProfile 
                });
                
                _logger.Write($"[POLLING] Remote profile changed detected");
            }
        }
        catch (Exception ex)
        {
            _logger.Write($"[POLLING] Error checking remote profile changes: {ex.Message}");
        }
    }

    private static bool HasProfileChanged(ClipboardProfileDTO? oldProfile, ClipboardProfileDTO? newProfile)
    {
        // 如果一个为null另一个不为null，则有变化
        if (oldProfile == null && newProfile != null) return true;
        if (oldProfile != null && newProfile == null) return true;
        if (oldProfile == null && newProfile == null) return false;

        // 比较关键字段
        return oldProfile!.File != newProfile!.File ||
               oldProfile.Clipboard != newProfile.Clipboard ||
               oldProfile.Type != newProfile.Type;
    }

    public void Dispose()
    {
        StopPolling();
        _configManager.ConfigChanged -= OnConfigChanged;
        _webDav?.Dispose();
    }
}