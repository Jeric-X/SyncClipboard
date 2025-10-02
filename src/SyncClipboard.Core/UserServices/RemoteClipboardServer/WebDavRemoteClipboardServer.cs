using SyncClipboard.Abstract;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Web;
using SyncClipboard.Core.Utilities.Image;
using SyncClipboard.Core.Exceptions;
using System.Text.Json;
using System.Net;
using System.Diagnostics.CodeAnalysis;
namespace SyncClipboard.Core.UserServices.RemoteClipboardServer;

public sealed class WebDavRemoteClipboardServer : IRemoteClipboardServer
{
    #region constants
    private const string RemoteProfilePath = "SyncClipboard.json";
    private const string RemoteFileFolder = "file";
    private const string ServiceName = "WebDavServer";

    #endregion

    private readonly ILogger _logger;
    private readonly ConfigManager _configManager;
    private readonly IAppConfig _appConfig;
    private readonly ITrayIcon _trayIcon;

    private WebDav _webDav;

    // 轮询相关
    private Timer? _pollingTimer;
    private Profile _lastKnownProfile = new UnknownProfile();
    private readonly object _pollingLock = new object();
    private bool _isPolling = false;
    private CancellationTokenSource? _pollingCancellationTokenSource;

    // TestAlive定时器相关
    private Timer? _testAliveTimer;
    private readonly object _testAliveLock = new object();
    private bool _testAliveRunning = false;

    // 配置
    private SyncConfig _syncConfig;
    private ServerConfig _serverConfig;

    private event EventHandler<ProfileChangedEventArgs>? RemoteProfileChangedImpl;
    public event EventHandler<ProfileChangedEventArgs>? RemoteProfileChanged
    {
        add
        {
            RemoteProfileChangedImpl += value;
            if (RemoteProfileChangedImpl != null && !_isPolling)
            {
                StartPolling();
            }
        }
        remove
        {
            RemoteProfileChangedImpl -= value;
            if (RemoteProfileChangedImpl == null && _isPolling)
            {
                StopPolling();
            }
        }
    }

    private event EventHandler<PollStatusEventArgs>? PollStatusEventImpl;
    public event EventHandler<PollStatusEventArgs>? PollStatusEvent
    {
        add => PollStatusEventImpl += value;
        remove => PollStatusEventImpl -= value;
    }

    public WebDavRemoteClipboardServer(ILogger logger, ConfigManager configManager, IAppConfig appConfig, ITrayIcon trayIcon)
    {
        _logger = logger;
        _configManager = configManager;
        _appConfig = appConfig;
        _trayIcon = trayIcon;

        _syncConfig = configManager.GetConfig<SyncConfig>();
        _serverConfig = configManager.GetConfig<ServerConfig>();

        _webDav = CreateWebDavInstance();

        configManager.ConfigChanged += OnConfigChanged;
        _ = InitializeAsync();

        StartTestAliveBackgroudService();
    }

    private void OnConfigChanged()
    {
        _syncConfig = _configManager.GetConfig<SyncConfig>();
        _serverConfig = _configManager.GetConfig<ServerConfig>();

        _webDav?.Dispose();
        _webDav = CreateWebDavInstance();

        StartTestAliveBackgroudService();
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
        var webDav = new WebDav(credential, _appConfig, _syncConfig.TrustInsecureCertificate, _logger) { Timeout = timeout };

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

    private async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _webDav.CreateDirectory(RemoteFileFolder, cancellationToken);
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
            return await _webDav.Exist(GetBaseAddress(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Write($"Warning: Connection test failed: {ex.Message}");
            return false;
        }
    }

    public async Task<Profile> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var profileDto = await _webDav.GetJson<ClipboardProfileDTO>(RemoteProfilePath, cancellationToken);
            ArgumentNullException.ThrowIfNull(profileDto);
            _trayIcon.SetStatusString(ServiceName, "Running.");
            return GetProfileBy(profileDto);
        }
        catch (Exception ex) when (
            ex is JsonException ||
            ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound } ||
            ex is ArgumentException)
        {
            return await UploadAndReturnBlankProfile(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ThrowServerException("Failed to get remote profile", ex);
            return null!;
        }
    }

    private static Profile GetProfileBy(ClipboardProfileDTO profileDTO)
    {
        switch (profileDTO.Type)
        {
            case ProfileType.Text:
                return new TextProfile(profileDTO.Clipboard);
            case ProfileType.File:
                {
                    if (ImageHelper.FileIsImage(profileDTO.File))
                    {
                        return new ImageProfile(profileDTO);
                    }
                    return new FileProfile(profileDTO);
                }
            case ProfileType.Image:
                return new ImageProfile(profileDTO);
            case ProfileType.Group:
                return new GroupProfile(profileDTO);
        }

        return new UnknownProfile();
    }

    public async Task SetProfileAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            await CleanServerTempFile(cancellationToken);
            await UploadProfileDataAsync(profile, cancellationToken);
            await _webDav.PutJson(RemoteProfilePath, profile.ToDto(), cancellationToken);
            _lastKnownProfile = profile;

            _logger.Write($"[PUSH] Profile metadata updated: {JsonSerializer.Serialize(profile.ToDto())}");
            _trayIcon.SetStatusString(ServiceName, "Running.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ThrowServerException("Failed to set remote profile", ex);
        }
    }

    private async Task CleanServerTempFile(CancellationToken cancelToken)
    {
        if (_syncConfig.DeletePreviousFilesOnPush)
        {
            try
            {
                await _webDav.DirectoryDelete(RemoteFileFolder, cancelToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound)  // 如果文件夹不存在直接忽略
            {
            }
            await _webDav.CreateDirectory(RemoteFileFolder, cancelToken);
        }
    }

    public async Task<Profile> UploadAndReturnBlankProfile(CancellationToken cancellationToken = default)
    {
        try
        {
            var blankProfile = new TextProfile("");
            await SetProfileAsync(blankProfile, cancellationToken);
            return blankProfile;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ThrowServerException("Failed to set blank profile", ex);
            return null!;
        }
    }

    private async Task UploadProfileDataAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        if (!profile.HasDataFile)
        {
            return;
        }

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

            var remotePath = $"{RemoteFileFolder}/{profile.FileName}";
            await _webDav.PutFile(remotePath, localDataPath, cancellationToken);
            _logger.Write($"[PUSH] Upload completed for {profile.FileName}");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ThrowServerException("Failed to upload profile data", ex);
        }
    }

    public async Task DownloadProfileDataAsync(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!profile.HasDataFile)
        {
            return;
        }

        try
        {
            var remotePath = $"{RemoteFileFolder}/{profile.FileName}";

            var dataPath = profile.GetLocalDataPath();
            var directory = Path.GetDirectoryName(dataPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await _webDav.GetFile(remotePath, dataPath, progress, cancellationToken);
            await profile.CheckDownloadedData(cancellationToken);
            _logger.Write($"[PULL] Downloaded {profile.FileName} to {dataPath}");
            _trayIcon.SetStatusString(ServiceName, "Running.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ThrowServerException("Failed to download profile data", ex);
        }
    }

    public void StartPolling()
    {
        lock (_pollingLock)
        {
            if (_isPolling) return;
            _isPolling = true;
        }

        _pollingCancellationTokenSource = new CancellationTokenSource();
        _ = RunPollingLoopAsync(_pollingCancellationTokenSource.Token);
        _logger.Write("[POLLING] Started monitoring remote profile changes");
    }

    private async Task RunPollingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var configInterval = TimeSpan.FromSeconds(_syncConfig.IntervalTime);
            var minInterval = TimeSpan.FromSeconds(0.5); // 最小间隔0.5秒
            var pollingInterval = configInterval < minInterval ? minInterval : configInterval;
            var maxRetryCount = _syncConfig.RetryTimes;
            var currentRetryCount = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await CheckRemoteProfileChanges(cancellationToken);
                    currentRetryCount = 0;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    currentRetryCount++;
                    _logger.Write($"[POLLING] Error in polling loop (attempt {currentRetryCount}/{maxRetryCount}): {ex.Message}");

                    if (currentRetryCount >= maxRetryCount)
                    {
                        _logger.Write($"[POLLING] Max retry count ({maxRetryCount}) exceeded. Stopping polling.");

                        PollStatusEventImpl?.Invoke(this, new PollStatusEventArgs
                        {
                            Status = PollStatus.StoppedDueToNetworkIssues,
                            Message = $"Polling stopped after {maxRetryCount} failed attempts",
                            Exception = ex
                        });
                        break;
                    }
                }

                await Task.Delay(pollingInterval, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.Write($"[POLLING] Fatal error in polling loop: {ex.Message}");
        }
        finally
        {
            lock (_pollingLock)
            {
                _isPolling = false;
            }
            _logger.Write("[POLLING] Polling loop terminated");
        }
    }

    public void StopPolling()
    {
        lock (_pollingLock)
        {
            if (!_isPolling) return;
            _isPolling = false;
        }

        _pollingCancellationTokenSource?.Cancel();
        _pollingCancellationTokenSource?.Dispose();
        _pollingCancellationTokenSource = null;

        _pollingTimer?.Dispose();
        _pollingTimer = null;

        _logger.Write("[POLLING] Stopped monitoring remote profile changes");
    }

    private void StartTestAliveBackgroudService()
    {
        lock (_testAliveLock)
        {
            _testAliveTimer?.Dispose();
            _testAliveTimer = new Timer(OnTestAliveTimer, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
            _logger.Write("[TEST_ALIVE] Started TestAlive timer (interval: 10 seconds)");
        }
    }

    private async void OnTestAliveTimer(object? state)
    {
        lock (_testAliveLock)
        {
            if (_testAliveRunning) return;
            _testAliveRunning = true;
        }

        try
        {
            var isAlive = await TestConnectionAsync();

            if (isAlive)
            {
                lock (_pollingLock)
                {
                    if (RemoteProfileChangedImpl != null && !_isPolling)
                    {
                        _logger.Write("[TEST_ALIVE] Connection restored, resuming polling");

                        PollStatusEventImpl?.Invoke(this, new PollStatusEventArgs
                        {
                            Status = PollStatus.Resumed,
                            Message = "Network connection restored, polling resumed"
                        });

                        StartPolling();
                    }
                }
            }
            else
            {
                _logger.Write("[TEST_ALIVE] Connection test failed");
            }
        }
        finally
        {
            lock (_testAliveLock)
            {
                _testAliveRunning = false;
            }
        }
    }

    private void StopTestAliveTimer()
    {
        lock (_testAliveLock)
        {
            _testAliveTimer?.Dispose();
            _testAliveTimer = null;
            _logger.Write("[TEST_ALIVE] Stopped TestAlive timer");
        }
    }

    private async Task CheckRemoteProfileChanges(CancellationToken cancellationToken = default)
    {
        var currentProfile = await GetProfileAsync(cancellationToken);

        if (!Profile.Same(_lastKnownProfile, currentProfile))
        {
            var oldProfile = _lastKnownProfile;
            _lastKnownProfile = currentProfile;

            RemoteProfileChangedImpl?.Invoke(this, new ProfileChangedEventArgs
            {
                NewProfile = currentProfile,
                OldProfile = oldProfile
            });

            _logger.Write($"[POLLING] Remote profile changed detected");
        }
    }

    private void SetLastErrorStatus(string message, Exception? innerException = null)
    {
        var statusMessage = $"Server Error: {message}";
        if (innerException != null)
        {
            statusMessage = $"{message}\n{innerException.Message}";
        }
        _logger.Write(statusMessage);
        _trayIcon.SetStatusString(ServiceName, statusMessage);
    }

    [DoesNotReturn]
    private void ThrowServerException(string message, Exception? innerException = null)
    {
        SetLastErrorStatus(message, innerException);

        _lastKnownProfile = new UnknownProfile();

        throw new RemoteServerException(message, innerException!);
    }

    public void Dispose()
    {
        StopPolling();
        StopTestAliveTimer();
        _configManager.ConfigChanged -= OnConfigChanged;
        _webDav?.Dispose();
    }
}