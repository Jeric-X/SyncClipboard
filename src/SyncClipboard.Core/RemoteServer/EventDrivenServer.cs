using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter;

namespace SyncClipboard.Core.RemoteServer;

public sealed class EventDrivenServer : IRemoteClipboardServer
{
    private readonly StorageBasedServerHelper _serverHelper;
    private readonly TestAliveHelper _testAliveHelper;
    private readonly IEventServerAdapter _serverAdapter;
    private readonly ITrayIcon _trayIcon;
    private readonly ILogger _logger;
    private bool _disconnected = true;

    public EventDrivenServer(IServiceProvider sp, IEventServerAdapter serverAdapter)
    {
        _serverHelper = new StorageBasedServerHelper(sp, serverAdapter);
        _serverAdapter = serverAdapter;
        _logger = sp.GetRequiredService<ILogger>();
        _trayIcon = sp.GetRequiredService<ITrayIcon>();
        _testAliveHelper = new TestAliveHelper(TestConnectionAsync);
        _testAliveHelper.TestSuccessed += OnTestAliveSuccessed;
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
    }

    private void ServerDisconnected(Exception? ex)
    {
        _disconnected = true;
        var message = "Disconnected from server";
        _serverHelper.SetErrorStatus(message, ex);

        PollStatusEventImpl?.Invoke(this, new PollStatusEventArgs
        {
            Status = PollStatus.StoppedDueToNetworkIssues,
            Message = message,
            Exception = ex
        });
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
        var newProfile = newProfileDto != null ? StorageBasedServerHelper.GetProfileBy(newProfileDto) : new UnknownProfile();
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

    public void Dispose()
    {
        _testAliveHelper.Dispose();
        _serverAdapter.ProfileDtoChanged -= OnProfileDtoChanged;
        _serverAdapter.StopListening();
        if (_serverAdapter is IDisposable disposableAdapter)
        {
            disposableAdapter.Dispose();
        }
    }
}