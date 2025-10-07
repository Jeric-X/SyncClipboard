using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter.WebDavServer;
using SyncClipboard.Server.Core.Constants;
using System.Text;

namespace SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;

public sealed class OfficialAdapter(
    ILogger logger,
    IAppConfig appConfig,
    [FromKeyedServices(WebDavConfig.TYPE_NAME)] IServerAdapter webDavAdapter)
    : IServerAdapter<OfficialConfig>, IEventServerAdapter, IDisposable
{
    private readonly ILogger _logger = logger;
    private readonly IAppConfig _appConfig = appConfig;
    private readonly WebDavAdapter _webDavAdapter = (WebDavAdapter)webDavAdapter;
    private readonly object _hubLock = new object();
    private HubConnection? _hubConnection;
    private OfficialConfig _officialConfig = new OfficialConfig();

    public event Action<ClipboardProfileDTO>? ProfileDtoChanged;
    public event Action<Exception?>? ServerDisconnected;
    public event Action? ServerConnected;

    public void OnConfigChanged(OfficialConfig config, SyncConfig syncConfig)
    {
        _officialConfig = config;

        ReConnectSignalR();
        _webDavAdapter.OnConfigChanged(new WebDavConfig
        {
            RemoteURL = config.RemoteURL,
            UserName = config.UserName,
            Password = config.Password,
            DeletePreviousFilesOnPush = config.DeletePreviousFilesOnPush
        }, syncConfig);
    }

    private void ReConnectSignalR()
    {
        DisconnectSignalR();
        lock (_hubLock)
        {
            if (_hubConnection != null)
                return;

            var serverUrl = _officialConfig.RemoteURL.TrimEnd('/');
            var signalRUrl = $"{serverUrl}{SignalRConstants.HubPath}";
            if (string.IsNullOrWhiteSpace(signalRUrl)) return;
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(new Uri(signalRUrl), config =>
                {
                    var base64 = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_officialConfig.UserName}:{_officialConfig.Password}"));
                    config.Headers.Add("Authorization", "Basic " + base64);
                })
                .Build();
        }

        _hubConnection.On<ClipboardProfileDTO>(SignalRConstants.RemoteProfileChangedMethod, profile =>
        {
            ProfileDtoChanged?.Invoke(profile);
        });
        StartSignalRConnectiron(_hubConnection);
    }

    private async void StartSignalRConnectiron(HubConnection hubConnection)
    {
        try
        {
            await hubConnection.StartAsync();
            hubConnection.Closed += arg =>
            {
                ServerDisconnected?.Invoke(arg);
                return Task.CompletedTask;
            };
            ServerConnected?.Invoke();
        }
        catch (Exception ex)
        {
            ServerDisconnected?.Invoke(ex);
            _logger.Write("OfficialAdapter", $"SignalR连接失败: {ex.Message}");
        }
    }

    private void DisconnectSignalR()
    {
        var old = null as HubConnection;
        lock (_hubLock)
        {
            old = _hubConnection;
            _hubConnection = null;
        }

        old?.StopAsync().Wait();
    }

    public Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return _webDavAdapter.TestConnectionAsync(cancellationToken);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return _webDavAdapter.InitializeAsync(cancellationToken);
    }

    public Task<ClipboardProfileDTO?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        return _webDavAdapter.GetProfileAsync(cancellationToken);
    }

    public Task SetProfileAsync(ClipboardProfileDTO profileDto, CancellationToken cancellationToken = default)
    {
        return _webDavAdapter.SetProfileAsync(profileDto, cancellationToken);
    }

    public Task DownloadFileAsync(string fileName, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return _webDavAdapter.DownloadFileAsync(fileName, localPath, progress, cancellationToken);
    }

    public Task UploadFileAsync(string fileName, string localPath, CancellationToken cancellationToken = default)
    {
        return _webDavAdapter.UploadFileAsync(fileName, localPath, cancellationToken);
    }

    public Task CleanupTempFilesAsync(CancellationToken cancellationToken = default)
    {
        return _webDavAdapter.CleanupTempFilesAsync(cancellationToken);
    }

    public void Dispose()
    {
        DisconnectSignalR();
        _webDavAdapter.Dispose();
    }

    public void StartListening()
    {
        ReConnectSignalR();
    }

    public void StopListening()
    {
        DisconnectSignalR();
    }
}
