using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter.WebDavServer;
using SyncClipboard.Server.Core.Constants;
using SyncClipboard.Server.Core.Models;
using System.Text;
using System.Text.Json;
using System.Web;

namespace SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;

public sealed class OfficialAdapter(
    ILogger logger,
    IAppConfig appConfig,
    [FromKeyedServices(WebDavConfig.ConfigTypeName)] IServerAdapter webDavAdapter)
    : IServerAdapter<OfficialConfig>, IEventServerAdapter, IHistorySyncServer, IDisposable
{
    private readonly ILogger _logger = logger;
    private readonly IAppConfig _appConfig = appConfig;
    private readonly WebDavAdapter _webDavAdapter = (WebDavAdapter)webDavAdapter;
    private readonly object _hubLock = new object();
    private readonly object _httpClientLock = new object();
    private HubConnection? _hubConnection;
    private OfficialConfig _officialConfig = new OfficialConfig();
    private HttpClient _httpClient = new HttpClient();

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
        ReCreateHttpClient();
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
        _httpClient?.Dispose();
    }

    public void StartListening()
    {
        ReConnectSignalR();
    }

    public void StopListening()
    {
        DisconnectSignalR();
    }

    public async Task<IEnumerable<HistoryRecordDto>> GetHistoryAsync(int page = 1, long? before = null, long? after = null, string? cursorProfileId = null, ProfileTypeFilter types = ProfileTypeFilter.All, string? searchText = null, bool? starred = null)
    {
        try
        {
            var uriBuilder = new UriBuilder($"{_officialConfig.RemoteURL.TrimEnd('/')}/api/history");
            var queryParams = new List<string>();

            if (page > 0)
                queryParams.Add($"page={page}");
            if (before.HasValue)
                queryParams.Add($"before={before.Value}");
            if (after.HasValue)
                queryParams.Add($"after={after.Value}");
            if (!string.IsNullOrWhiteSpace(cursorProfileId))
                queryParams.Add($"cursorProfileId={HttpUtility.UrlEncode(cursorProfileId)}");
            if (types != ProfileTypeFilter.All)
                queryParams.Add($"types={(int)types}");
            if (!string.IsNullOrWhiteSpace(searchText))
                queryParams.Add($"q={HttpUtility.UrlEncode(searchText)}");
            if (starred.HasValue)
                queryParams.Add($"starred={(starred.Value ? "true" : "false")}");

            if (queryParams.Count > 0)
                uriBuilder.Query = string.Join("&", queryParams);

            var response = await _httpClient.GetAsync(uriBuilder.Uri);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync();
            var records = await JsonSerializer.DeserializeAsync<List<HistoryRecordDto>>(stream, JsonSerializerOptions.Web);

            return records ?? [];
        }
        catch (Exception ex)
        {
            _logger.Write($"[OFFICIAL_ADAPTER] Failed to get history: {ex.Message}");
            throw;
        }
    }

    private void ReCreateHttpClient()
    {
        lock (_httpClientLock)
        {
            _httpClient.Dispose();

            var handler = new HttpClientHandler();
            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(_officialConfig.RemoteURL.TrimEnd('/') + '/')
            };

            var base64 = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_officialConfig.UserName}:{_officialConfig.Password}"));
            _httpClient.DefaultRequestHeaders.Add("Authorization", "Basic " + base64);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SyncClipboard");
        }
    }
}
