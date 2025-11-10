using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Core.RemoteServer;

public sealed class EventDrivenServer : IRemoteClipboardServer, IHistorySyncServer
{
    private readonly StorageBasedServerHelper _serverHelper;
    private readonly TestAliveHelper _testAliveHelper;
    private readonly IEventServerAdapter _serverAdapter;
    private readonly ITrayIcon _trayIcon;
    private readonly ILogger _logger;
    private readonly SingletonTask _singletonQueryTask = new SingletonTask();
    private bool _disconnected = true;

    public EventDrivenServer(IServiceProvider sp, IEventServerAdapter serverAdapter)
    {
        _serverHelper = new StorageBasedServerHelper(sp, serverAdapter);
        _serverAdapter = serverAdapter;
        _logger = sp.GetRequiredService<ILogger>();
        _trayIcon = sp.GetRequiredService<ITrayIcon>();
        _testAliveHelper = new TestAliveHelper(TestConnectionAsync);
        _testAliveHelper.TestSuccessed += OnTestAliveSuccessed;
        _serverHelper.ExceptionOccurred += OnServerHelperExceptionOccurred;
        _testAliveHelper.Restart();
        _serverAdapter.ServerDisconnected += ServerDisconnected;
        _serverAdapter.ServerConnected += ServerConnected;
    }

    private void ServerConnected()
    {
        PollStatusEventImpl?.Invoke(this, new PollStatusEventArgs
        {
            Status = PollStatus.Resumed,
            Message = "Network connection restored, resumed"
        });
        _disconnected = false;
        _trayIcon.SetStatusString(ServerConstants.StatusName, "Running.");
        _ = _singletonQueryTask.Run(QueryOnce);
    }

    private async Task QueryOnce(CancellationToken cancellationToken)
    {
        try
        {
            var profile = await GetProfileAsync(cancellationToken).ConfigureAwait(false);
            RemoteProfileChangedImpl?.Invoke(this, new ProfileChangedEventArgs
            {
                NewProfile = profile
            });
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            SetErrorStatus("Get remote clipboard failed", ex);
        }
    }

    private void SetErrorStatus(string message, Exception? ex = null)
    {
        _disconnected = true;
        _serverHelper.SetErrorStatus(message, ex);
        PollStatusEventImpl?.Invoke(this, new PollStatusEventArgs
        {
            Status = PollStatus.StoppedDueToNetworkIssues,
            Message = message,
            Exception = ex
        });
    }

    private void ServerDisconnected(Exception? ex)
    {
        var message = "Disconnected from server";
        SetErrorStatus(message, ex);
    }

    private event EventHandler<ProfileChangedEventArgs>? RemoteProfileChangedImpl;
    public event EventHandler<ProfileChangedEventArgs>? RemoteProfileChanged
    {
        add
        {
            bool wasEmpty = RemoteProfileChangedImpl == null;
            RemoteProfileChangedImpl += value;
            if (wasEmpty && RemoteProfileChangedImpl != null)
            {
                _serverAdapter.ProfileDtoChanged += OnProfileDtoChanged;
            }
        }
        remove
        {
            RemoteProfileChangedImpl -= value;
            if (RemoteProfileChangedImpl == null)
            {
                _serverAdapter.ProfileDtoChanged -= OnProfileDtoChanged;
            }
        }
    }

    private event EventHandler<PollStatusEventArgs>? PollStatusEventImpl;
    public event EventHandler<PollStatusEventArgs>? PollStatusEvent
    {
        add => PollStatusEventImpl += value;
        remove => PollStatusEventImpl -= value;
    }

    private void OnProfileDtoChanged(ClipboardProfileDTO? newProfileDto)
    {
        var newProfile = newProfileDto != null ? ClipboardProfileDTO.CreateProfile(newProfileDto) : new UnknownProfile();
        RemoteProfileChangedImpl?.Invoke(this, new ProfileChangedEventArgs
        {
            NewProfile = newProfile,
        });
        _logger.Write($"[EVENT] Remote profile changed detected");
    }


    public Task DownloadProfileDataAsync(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return _serverHelper.DownloadProfileDataAsync(profile, progress, cancellationToken);
    }

    public Task<Profile> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        return _serverHelper.GetProfileAsync(cancellationToken);
    }

    public void OnSyncConfigChanged(SyncConfig syncConfig)
    {
        _testAliveHelper.Restart();
    }

    public Task SetProfileAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        return _serverHelper.SetProfileAsync(profile, cancellationToken);
    }

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return _serverHelper.TestConnectionAsync(cancellationToken);
    }

    private void OnTestAliveSuccessed()
    {
        if (_disconnected)
        {
            _serverAdapter.StartListening();
        }
    }

    private void OnServerHelperExceptionOccurred()
    {
        _disconnected = true;
    }

    public void Dispose()
    {
        _serverAdapter.ServerDisconnected -= ServerDisconnected;
        _serverAdapter.ServerConnected -= ServerConnected;
        _serverAdapter.ProfileDtoChanged -= OnProfileDtoChanged;
        _serverHelper.ExceptionOccurred -= OnServerHelperExceptionOccurred;
        _serverAdapter.StopListening();
        _testAliveHelper.Dispose();
        if (_serverAdapter is IDisposable disposableAdapter)
        {
            disposableAdapter.Dispose();
        }
    }

    public Task<IEnumerable<HistoryRecordDto>> GetHistoryAsync(int page = 1, long? before = null, long? after = null, string? cursorProfileId = null, ProfileTypeFilter types = ProfileTypeFilter.All, string? searchText = null, bool? starred = null)
    {
        if (_serverAdapter is not IHistorySyncServer syncServer)
        {
            throw new NotSupportedException("The current server adapter does not support history sync.");
        }
        return syncServer.GetHistoryAsync(page, before, after, cursorProfileId, types, searchText, starred);
    }

    public Task DownloadHistoryDataAsync(string profileId, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_serverAdapter is not IHistorySyncServer syncServer)
        {
            throw new NotSupportedException("The current server adapter does not support history sync.");
        }
        return syncServer.DownloadHistoryDataAsync(profileId, localPath, progress, cancellationToken);
    }

    public Task UpdateHistoryAsync(ProfileType type, string hash, HistoryRecordUpdateDto dto, CancellationToken cancellationToken = default)
    {
        if (_serverAdapter is not IHistorySyncServer syncServer)
        {
            throw new NotSupportedException("The current server adapter does not support history sync.");
        }
        return syncServer.UpdateHistoryAsync(type, hash, dto, cancellationToken);
    }

    public Task UploadHistoryAsync(ProfileType type, string hash, HistoryRecordUpdateDto dto, DateTimeOffset createTime, string? filePath = null, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_serverAdapter is not IHistorySyncServer syncServer)
        {
            throw new NotSupportedException("The current server adapter does not support history sync.");
        }
        return syncServer.UploadHistoryAsync(type, hash, dto, createTime, filePath, progress, cancellationToken);
    }
}