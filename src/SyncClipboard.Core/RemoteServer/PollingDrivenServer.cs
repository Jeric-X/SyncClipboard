using SyncClipboard.Abstract;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Image;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.I18n;
using System.Text.Json;
using System.Net;
using System.Diagnostics.CodeAnalysis;
using SyncClipboard.Core.RemoteServer.Adapter;

namespace SyncClipboard.Core.RemoteServer;

public sealed class PollingDrivenServer : IRemoteClipboardServer
{
    #region constants
    private static readonly string ServiceName = Strings.ConnectionDetails;
    #endregion

    private readonly ILogger _logger;
    private readonly ConfigManager _configManager;
    private readonly ITrayIcon _trayIcon;
    private readonly IPollingServerAdapter _serverAdapter;

    // 轮询相关
    private Profile _lastKnownProfile = new UnknownProfile();
    private readonly object _pollingLock = new object();
    private bool _isPolling = false;
    private CancellationTokenSource? _pollingCancellationTokenSource;

    // TestAlive任务相关
    private Task? _testAliveTask;
    private CancellationTokenSource? _testAliveCancellationTokenSource;
    private readonly object _testAliveLock = new object();

    // 配置
    private SyncConfig _syncConfig;

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

    public PollingDrivenServer(ILogger logger, ConfigManager configManager, ITrayIcon trayIcon, IPollingServerAdapter serverAdapter)
    {
        _logger = logger;
        _configManager = configManager;
        _trayIcon = trayIcon;
        _serverAdapter = serverAdapter;

        _syncConfig = configManager.GetConfig<SyncConfig>();

        configManager.ConfigChanged += OnConfigChanged;
        _ = InitializeAsync();

        StartTestAliveBackgroudService();
    }

    public void OnAdapterConfigChanged()
    {
        StartTestAliveBackgroudService();
    }

    private void OnConfigChanged()
    {
        var oldConfig = _syncConfig;
        if (_syncConfig != oldConfig)
        {
            _syncConfig = _configManager.GetConfig<SyncConfig>();
            StartTestAliveBackgroudService();
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _serverAdapter.InitializeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Write($"Warning: Failed to initialize server adapter: {ex.Message}");
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _serverAdapter.TestConnectionAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Write($"Warning: Connection test failed: {ex.Message}");
            return false;
        }
    }

    public async Task<Profile> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var profileDto = await _serverAdapter.GetProfileAsync(cancellationToken);
            if (profileDto == null)
            {
                return await UploadAndReturnBlankProfile(cancellationToken);
            }

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
            await _serverAdapter.CleanupTempFilesAsync(cancellationToken);
            await UploadProfileDataAsync(profile, cancellationToken);
            await _serverAdapter.SetProfileAsync(profile.ToDto(), cancellationToken);
            _lastKnownProfile = profile;

            _logger.Write($"[PUSH] Profile metadata updated: {JsonSerializer.Serialize(profile.ToDto())}");
            _trayIcon.SetStatusString(ServiceName, "Running.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ThrowServerException("Failed to set remote profile", ex);
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

            await _serverAdapter.UploadFileAsync(profile.FileName, localDataPath, cancellationToken);
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
            var dataPath = profile.GetLocalDataPath();
            await _serverAdapter.DownloadFileAsync(profile.FileName, dataPath, progress, cancellationToken);
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
            if (_isPolling)
                return;
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

        _logger.Write("[POLLING] Stopped monitoring remote profile changes");
    }

    private void StartTestAliveBackgroudService()
    {
        if (isDisposed) return;

        StopTestAliveTask();
        lock (_testAliveLock)
        {
            _testAliveCancellationTokenSource = new CancellationTokenSource();
            _testAliveTask = RunTestAliveLoopAsync(_testAliveCancellationTokenSource.Token);
        }
    }

    private void ResumePolling()
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

    private async Task RunTestAliveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var testAliveInterval = TimeSpan.FromSeconds(10);

            while (!cancellationToken.IsCancellationRequested && !isDisposed)
            {
                var isAlive = await TestConnectionAsync(cancellationToken);
                if (isAlive)
                {
                    ResumePolling();
                }
                else
                {
                    _logger.Write("[TEST_ALIVE] Connection test failed");
                }

                await Task.Delay(testAliveInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Write("[TEST_ALIVE] TestAlive task was cancelled");
        }
    }

    private void StopTestAliveTask()
    {
        lock (_testAliveLock)
        {
            _testAliveCancellationTokenSource?.Cancel();
            _testAliveCancellationTokenSource?.Dispose();
            _testAliveCancellationTokenSource = null;
            _testAliveTask = null;
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

        throw new RemoteServerException(message, innerException);
    }

    bool isDisposed = false;
    public void Dispose()
    {
        isDisposed = true;
        StopPolling();
        StopTestAliveTask();
    }
}