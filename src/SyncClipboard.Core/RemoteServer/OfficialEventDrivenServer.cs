using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.RemoteServer;

public sealed class OfficialEventDrivenServer : IRemoteClipboardServer, IOfficialSyncServer
{
    private readonly StorageBasedServerHelper _serverHelper;
    private readonly TestAliveHelper _testAliveHelper;
    private readonly IOfficialServerAdapter _serverAdapter;
    private readonly ITrayIcon _trayIcon;
    private readonly ILogger _logger;
    private readonly HistoryTransferQueue _historyTransferQueue;
    private readonly SingletonTask _singletonQueryTask = new SingletonTask();
    private bool _disconnected = true;

    public event Action<HistoryRecordDto>? HistoryChanged;

    public OfficialEventDrivenServer(IServiceProvider sp, IOfficialServerAdapter serverAdapter)
    {
        _serverHelper = new StorageBasedServerHelper(sp, serverAdapter);
        _serverAdapter = serverAdapter;
        _logger = sp.GetRequiredService<ILogger>();
        _trayIcon = sp.GetRequiredService<ITrayIcon>();
        _historyTransferQueue = sp.GetRequiredService<HistoryTransferQueue>();
        _testAliveHelper = new TestAliveHelper(TestConnectionAsync);
        _testAliveHelper.TestSuccessed += OnTestAliveSuccessed;
        _serverHelper.ExceptionOccurred += OnServerHelperExceptionOccurred;
        _testAliveHelper.Restart();
        _serverAdapter.ServerDisconnected += ServerDisconnected;
        _serverAdapter.ServerConnected += ServerConnected;

        if (_serverAdapter is IOfficialSyncServer historySyncServer)
        {
            historySyncServer.HistoryChanged += OnHistoryChanged;
        }
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

    private void OnProfileDtoChanged(ProfileDto newProfileDto)
    {
        var newProfile = Profile.Create(newProfileDto);
        RemoteProfileChangedImpl?.Invoke(this, new ProfileChangedEventArgs
        {
            NewProfile = newProfile,
        });
        _logger.Write($"[EVENT] Remote profile changed detected");
    }

    private void OnHistoryChanged(HistoryRecordDto historyRecordDto)
    {
        _logger.Write($"[EVENT] Remote history changed: {historyRecordDto.Type}/{historyRecordDto.Hash}");
        HistoryChanged?.Invoke(historyRecordDto);
    }

    public Task DownloadProfileDataAsync(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return _historyTransferQueue.Download(profile, progress, cancellationToken);
    }

    public async Task<Profile> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var profileDto = await _serverAdapter.GetCurrentProfileAsync(cancellationToken);
            if (profileDto is not null)
            {
                return Profile.Create(profileDto);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _serverHelper.ThrowServerException("Failed to get remote profile", ex);
        }
        _serverHelper.ThrowServerException("Failed to get remote profile");
        return null;
    }

    public void OnSyncConfigChanged(SyncConfig syncConfig)
    {
        _testAliveHelper.Restart();
    }

    public async Task SetProfileAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        await _historyTransferQueue.Upload(profile, null, cancellationToken);
        var dto = await profile.ToProfileDto(cancellationToken);
        await _serverAdapter.SetCurrentProfile(dto, cancellationToken);
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
        if (_serverAdapter is IOfficialSyncServer historySyncServer)
        {
            historySyncServer.HistoryChanged -= OnHistoryChanged;
        }
        _serverHelper.ExceptionOccurred -= OnServerHelperExceptionOccurred;
        _serverAdapter.StopListening();
        _testAliveHelper.Dispose();
        if (_serverAdapter is IDisposable disposableAdapter)
        {
            disposableAdapter.Dispose();
        }
    }

    public Task<HistoryRecordDto?> GetHistoryByProfileIdAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (_serverAdapter is not IOfficialSyncServer syncServer)
        {
            throw new NotSupportedException("The current server adapter does not support history sync.");
        }
        return syncServer.GetHistoryByProfileIdAsync(profileId, cancellationToken);
    }

    public Task<IEnumerable<HistoryRecordDto>> GetHistoryAsync(int page = 1, DateTimeOffset? before = null, DateTimeOffset? after = null, DateTimeOffset? modifiedAfter = null, ProfileTypeFilter types = ProfileTypeFilter.All, string? searchText = null, bool? starred = null, bool sortByLastAccessed = false)
    {
        if (_serverAdapter is not IOfficialSyncServer syncServer)
        {
            throw new NotSupportedException("The current server adapter does not support history sync.");
        }
        return syncServer.GetHistoryAsync(page, before, after, modifiedAfter, types, searchText, starred, sortByLastAccessed);
    }

    public Task DownloadHistoryDataAsync(string profileId, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_serverAdapter is not IOfficialSyncServer syncServer)
        {
            throw new NotSupportedException("The current server adapter does not support history sync.");
        }
        return syncServer.DownloadHistoryDataAsync(profileId, localPath, progress, cancellationToken);
    }

    public Task UpdateHistoryAsync(ProfileType type, string hash, HistoryRecordUpdateDto dto, CancellationToken cancellationToken = default)
    {
        if (_serverAdapter is not IOfficialSyncServer syncServer)
        {
            throw new NotSupportedException("The current server adapter does not support history sync.");
        }
        return syncServer.UpdateHistoryAsync(type, hash, dto, cancellationToken);
    }

    public Task UploadHistoryAsync(HistoryRecordDto dto, string? filePath = null, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_serverAdapter is not IOfficialSyncServer syncServer)
        {
            throw new NotSupportedException("The current server adapter does not support history sync.");
        }
        return syncServer.UploadHistoryAsync(dto, filePath, progress, cancellationToken);
    }

    public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
    {
        if (_serverAdapter is not IOfficialSyncServer syncServer)
        {
            throw new NotSupportedException("The current server adapter does not support history sync.");
        }
        return syncServer.GetServerTimeAsync(cancellationToken);
    }

    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        if (_serverAdapter is not IOfficialSyncServer syncServer)
        {
            throw new NotSupportedException("The current server adapter does not support history sync.");
        }
        return syncServer.GetVersionAsync(cancellationToken);
    }
}