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
using SyncClipboard.Core.Utilities.Web;
using System.Net.Http.Json;
using System.Net;

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

    public async Task UpdateHistoryAsync(ProfileType type, string hash, HistoryRecordUpdateDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = new Uri(_httpClient.BaseAddress!, $"api/history/{type}/{hash}");
            using var response = await _httpClient.PatchAsJsonAsync(url, dto, cancellationToken);
            var serverDto = await response.Content.ReadFromJsonAsync<HistoryRecordUpdateDto>(cancellationToken: cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return; // 成功不再返回 payload
            }
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                throw new SyncClipboard.Core.Exceptions.RemoteHistoryConflictException($"History update conflict {type}/{hash}", serverDto);
            }
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new SyncClipboard.Core.Exceptions.RemoteHistoryNotFoundException($"History record not found {type}/{hash}");
            }

            response.EnsureSuccessStatusCode();
            return; // unreachable
        }
        catch (Exception ex)
        {
            _logger.Write($"[OFFICIAL_ADAPTER] Failed to update history {type}/{hash}: {ex.Message}");
            throw;
        }
    }

    public async Task UploadHistoryAsync(ProfileType type, string hash, HistoryRecordUpdateDto dto, DateTimeOffset createTime, string? filePath = null, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = new Uri(_httpClient.BaseAddress!, $"api/history/{type}/{hash}");

            using var form = new MultipartFormDataContent();
            if (dto.Stared.HasValue) form.Add(new StringContent(dto.Stared.Value ? "true" : "false"), "stared");
            else form.Add(new StringContent("false"), "stared");
            if (dto.Pinned.HasValue) form.Add(new StringContent(dto.Pinned.Value ? "true" : "false"), "pinned");
            else form.Add(new StringContent("false"), "pinned");
            if (dto.IsDelete.HasValue) form.Add(new StringContent(dto.IsDelete.Value ? "true" : "false"), "isDelete");
            else form.Add(new StringContent("false"), "isDelete");
            form.Add(new StringContent((dto.Version ?? 1).ToString()), "version");
            var lm = dto.LastModified ?? DateTimeOffset.UtcNow;
            form.Add(new StringContent(lm.ToString("o")), "lastModified");
            form.Add(new StringContent(createTime.ToString("o")), "createTime");

            // 可选文件
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                var stream = File.OpenRead(filePath);
                HttpContent fileContent;
                if (progress is null)
                {
                    fileContent = new StreamContent(stream);
                }
                else
                {
                    fileContent = new ProgressableStreamContent(stream, progress, cancellationToken);
                }
                // 让服务器决定内容类型，或默认 application/octet-stream
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                var safeName = Path.GetFileName(filePath);
                form.Add(fileContent, "file", safeName);
            }

            using var response = await _httpClient.PutAsync(url, form, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                HistoryRecordUpdateDto? serverDto = null;
                try
                {
                    serverDto = await response.Content.ReadFromJsonAsync<HistoryRecordUpdateDto>(cancellationToken: cancellationToken);
                }
                catch { /* ignore parse errors, fall back to null */ }
                throw new SyncClipboard.Core.Exceptions.RemoteHistoryConflictException($"History already exists {type}/{hash}", serverDto);
            }
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.Write($"[OFFICIAL_ADAPTER] Failed to upload history {type}/{hash}: {ex.Message}");
            throw;
        }
    }

    private sealed class ProgressableStreamContent(Stream stream, IProgress<HttpDownloadProgress> progress, CancellationToken ct) : HttpContent
    {
        private const int DefaultBufferSize = 81920; // 80KB .NET default
        private readonly Stream _stream = stream;
        private readonly IProgress<HttpDownloadProgress> _progress = progress;
        private readonly CancellationToken _ct = ct;

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = new byte[DefaultBufferSize];
            long totalBytesRead = 0;
            int bytesRead;
            while ((bytesRead = await _stream.ReadAsync(buffer, _ct)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, bytesRead), _ct);
                totalBytesRead += bytesRead;
                _progress.Report(new HttpDownloadProgress
                {
                    BytesReceived = (ulong)totalBytesRead,
                    TotalBytesToReceive = null
                });
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            if (_stream.CanSeek)
            {
                length = _stream.Length;
                return true;
            }
            length = -1;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _stream.Dispose();
            }
        }
    }

    public async Task DownloadHistoryDataAsync(string profileId, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = new Uri(_httpClient.BaseAddress!, $"api/history/{HttpUtility.UrlEncode(profileId)}/data");
            if (progress is null)
            {
                await _httpClient.GetFile(url.ToString(), localPath, cancellationToken);
            }
            else
            {
                await _httpClient.GetFile(url.ToString(), localPath, progress, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.Write($"[OFFICIAL_ADAPTER] Failed to download history data for {profileId}: {ex.Message}");
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
