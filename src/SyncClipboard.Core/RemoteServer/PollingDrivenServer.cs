using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter;

namespace SyncClipboard.Core.RemoteServer;

public sealed class PollingDrivenServer : IRemoteClipboardServer
{
    private readonly ILogger _logger;
    private readonly IStorageBasedServerAdapter _serverAdapter;

    // 轮询相关
    private Profile _lastKnownProfile = new UnknownProfile();
    private readonly object _pollingLock = new object();
    private bool _isPolling = false;
    private CancellationTokenSource? _pollingCancellationTokenSource;

    // Helpers
    private readonly TestAliveHelper _testAliveHelper;
    private readonly StorageBasedServerHelper _storageHelper;

    // 配置
    private SyncConfig _syncConfig = new();

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

    public PollingDrivenServer(IServiceProvider sp, IStorageBasedServerAdapter serverAdapter)
    {
        _logger = sp.GetRequiredService<ILogger>();
        _serverAdapter = serverAdapter;

        _storageHelper = new StorageBasedServerHelper(sp, serverAdapter);
        _storageHelper.ExceptionOccurred += () => _lastKnownProfile = new UnknownProfile();

        _testAliveHelper = new TestAliveHelper(TestConnectionAsync);
        _testAliveHelper.TestSuccessed += ResumePolling;
        StartTestAliveBackgroudService();
    }

    public void OnSyncConfigChanged(SyncConfig syncConfig)
    {
        _syncConfig = syncConfig;
        StartTestAliveBackgroudService();
    }

    public Task<Profile> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        return _storageHelper.GetProfileAsync(cancellationToken);
    }

    public Task SetProfileAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        return _storageHelper.SetProfileAsync(profile, cancellationToken);
    }

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return _storageHelper.TestConnectionAsync(cancellationToken);
    }

    public Task DownloadProfileDataAsync(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return _storageHelper.DownloadProfileDataAsync(profile, progress, cancellationToken);
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
        if (isDisposed)
            return;

        _testAliveHelper.Restart();
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
            });

            _logger.Write($"[POLLING] Remote profile changed detected");
        }
    }


    bool isDisposed = false;
    public void Dispose()
    {
        isDisposed = true;
        StopPolling();
        _testAliveHelper.TestSuccessed -= ResumePolling;
        _testAliveHelper.Dispose();
        if (_serverAdapter is IDisposable disposableAdapter)
        {
            disposableAdapter.Dispose();
        }
    }
}