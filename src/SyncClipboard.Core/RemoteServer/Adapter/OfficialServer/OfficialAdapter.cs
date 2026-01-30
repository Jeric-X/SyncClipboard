using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter.WebDavServer;
using SyncClipboard.Server.Core.Constants;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Hubs;
using System.Text;
using System.Text.Json;
using System.Web;
using SyncClipboard.Core.Utilities.Web;
using System.Net.Http.Json;
using System.Net;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Utilities.Updater;

namespace SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;

public sealed class OfficialAdapter(
    ILogger logger,
    IAppConfig appConfig,
    [FromKeyedServices(WebDavConfig.ConfigTypeName)] IServerAdapter webDavAdapter)
    : IServerAdapter<OfficialConfig>, IOfficialServerAdapter, IOfficialSyncServer, IDisposable
{
    private readonly ILogger _logger = logger;
    private readonly IAppConfig _appConfig = appConfig;
    private readonly WebDavAdapter _webDavAdapter = (WebDavAdapter)webDavAdapter;
    private readonly object _hubLock = new object();
    private readonly object _httpClientLock = new object();
    private HubConnection? _hubConnection;
    private OfficialConfig _officialConfig = new OfficialConfig();
    private HttpClient _httpClient = new HttpClient();

    public event Action<ProfileDto>? ProfileDtoChanged;
    public event Action<HistoryRecordDto>? HistoryChanged;
    public event Action<Exception?>? ServerDisconnected;
    public event Action? ServerConnected;

    public void SetConfig(OfficialConfig config, SyncConfig syncConfig)
    {
        _officialConfig = config;
        _webDavAdapter.SetConfig(new WebDavConfig
        {
            RemoteURL = config.RemoteURL,
            UserName = config.UserName,
            Password = config.Password,
            DeletePreviousFilesOnPush = config.DeletePreviousFilesOnPush
        }, syncConfig);
    }

    public void ApplyConfig()
    {
        try
        {
            ReConnectSignalR();
            _webDavAdapter.ApplyConfig();
            ReCreateHttpClient();
        }
        catch (Exception ex)
        {
            ServerDisconnected?.Invoke(ex);
            _logger.Write("OfficialAdapter", $"Connection failed: {ex.Message}");
            _hubConnection = null;
            return;
        }
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

        _hubConnection.On<ProfileDto>(nameof(ISyncClipboardClient.RemoteProfileChanged), profile =>
        {
            ProfileDtoChanged?.Invoke(profile);
        });

        _hubConnection.On<HistoryRecordDto>(nameof(ISyncClipboardClient.RemoteHistoryChanged), historyRecord =>
        {
            HistoryChanged?.Invoke(historyRecord);
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
            await _logger.WriteAsync("OfficialAdapter", $"SignalR连接失败: {ex.Message}");
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

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _webDavAdapter.TestConnectionAsync(cancellationToken);

        // 验证服务器版本不能低于客户端版本
        try
        {
            var serverVersion = await GetVersionAsync(cancellationToken);
            var requestVersion = Env.RequestServerVersion;

            if (AppVersion.TryParse(serverVersion, out var serverVer) && AppVersion.TryParse(requestVersion, out var requestVer))
            {
                if (serverVer < requestVer)
                {
                    throw new InvalidOperationException($"Server version ({serverVersion}) is lower than requested version ({requestVersion}). Please upgrade your server.");
                }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to verify server version.", ex);
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return _webDavAdapter.InitializeAsync(cancellationToken);
    }

    public Task<ProfileDto?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        return _webDavAdapter.GetProfileAsync(cancellationToken);
    }

    public Task SetProfileAsync(ProfileDto profileDto, CancellationToken cancellationToken = default)
    {
        return _webDavAdapter.SetProfileAsync(profileDto, cancellationToken);
    }

    public Task DownloadFileAsync(string fileName, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return _webDavAdapter.DownloadFileAsync(fileName, localPath, progress, cancellationToken);
    }

    public Task UploadFileAsync(string fileName, string localPath, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return _webDavAdapter.UploadFileAsync(fileName, localPath, progress, cancellationToken);
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

    public async Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{_officialConfig.RemoteURL.TrimEnd('/')}/api/time";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<DateTimeOffset>(JsonSerializerOptions.Web, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Write($"[OFFICIAL_ADAPTER] Failed to get server time: {ex.Message}");
            throw;
        }
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{_officialConfig.RemoteURL.TrimEnd('/')}/api/version";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadAsStringAsync(cancellationToken);
            return result ?? "Unknown";
        }
        catch (Exception ex)
        {
            _logger.Write($"[OFFICIAL_ADAPTER] Failed to get version: {ex.Message}");
            throw;
        }
    }

    public async Task<IEnumerable<HistoryRecordDto>> GetHistoryAsync(int page = 1, DateTimeOffset? before = null, DateTimeOffset? after = null, DateTimeOffset? modifiedAfter = null, ProfileTypeFilter types = ProfileTypeFilter.All, string? searchText = null, bool? starred = null, bool sortByLastAccessed = false)
    {
        try
        {
            var url = new Uri(_httpClient.BaseAddress!, "api/history/query");

            using var content = new MultipartFormDataContent
            {
                { new StringContent(page.ToString()), nameof(HistoryQueryDto.Page) },
                { new StringContent(before?.ToString() ?? string.Empty), nameof(HistoryQueryDto.Before) },
                { new StringContent(after?.ToString() ?? string.Empty), nameof(HistoryQueryDto.After) },
                { new StringContent(modifiedAfter?.ToString() ?? string.Empty), nameof(HistoryQueryDto.ModifiedAfter) },
                { new StringContent(types.ToString()), nameof(HistoryQueryDto.Types) },
                { new StringContent(searchText ?? string.Empty), nameof(HistoryQueryDto.SearchText) },
                { new StringContent(starred?.ToString() ?? string.Empty), nameof(HistoryQueryDto.Starred) },
                { new StringContent(sortByLastAccessed.ToString()), nameof(HistoryQueryDto.SortByLastAccessed) }
            };

            var response = await _httpClient.PostAsync(url, content);
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

    public async Task<HistoryRecordDto?> GetHistoryByProfileIdAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = new Uri(_httpClient.BaseAddress!, $"api/history/{HttpUtility.UrlEncode(profileId)}");
            using var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var dto = await response.Content.ReadFromJsonAsync<HistoryRecordDto>(cancellationToken: cancellationToken);
            if (dto is not null && dto.IsDeleted)
            {
                return null;
            }
            return dto;
        }
        catch (Exception ex)
        {
            _logger.Write($"[OFFICIAL_ADAPTER] Failed to get history by profileId {profileId}: {ex.Message}");
            throw;
        }
    }

    public async Task UpdateHistoryAsync(ProfileType type, string hash, HistoryRecordUpdateDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = new Uri(_httpClient.BaseAddress!, $"api/history/{type}/{hash}");
            using var response = await _httpClient.PatchAsJsonAsync(url, dto, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return; // 成功不再返回 payload
            }
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var serverDto = await response.Content.ReadFromJsonAsync<HistoryRecordUpdateDto>(cancellationToken: cancellationToken);
                throw new RemoteHistoryConflictException($"History update conflict {type}/{hash}", serverDto);
            }
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new RemoteHistoryNotFoundException($"History record not found {type}/{hash}");
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

    public async Task UploadHistoryAsync(HistoryRecordDto dto, string? filePath = null, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = new Uri(_httpClient.BaseAddress!, $"api/history");
            using var content = new MultipartFormDataContent
            {
                // 添加元数据字段
                { new StringContent(dto.Hash), "hash" },
                { new StringContent(dto.Type.ToString()), "type" },
                { new StringContent(dto.CreateTime.ToString("o")), "createTime" },
                { new StringContent(dto.LastModified.ToString("o")), "lastModified" },
                { new StringContent(dto.LastAccessed.ToString("o")), "lastAccessed" },
                { new StringContent(dto.Starred.ToString()), "starred" },
                { new StringContent(dto.Pinned.ToString()), "pinned" },
                { new StringContent(dto.Version.ToString()), "version" },
                { new StringContent(dto.IsDeleted.ToString()), "isDeleted" },
                { new StringContent(dto.Text), "text" },
                { new StringContent(dto.Size.ToString()), "size" }
            };

            // 添加文件字段（如果提供）
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                var stream = File.OpenRead(filePath);
                HttpContent fileContent = progress is null
                    ? new StreamContent(stream)
                    : new ProgressableStreamContent(stream, progress, cancellationToken);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, "data", Path.GetFileName(filePath));
            }

            using var response = await _httpClient.PostAsync(url, content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                HistoryRecordUpdateDto? serverDto = null;
                try
                {
                    serverDto = await response.Content.ReadFromJsonAsync<HistoryRecordUpdateDto>(cancellationToken: cancellationToken);
                }
                catch { /* ignore parse errors, fall back to null */ }
                throw new RemoteHistoryConflictException($"History already exists {dto.Type}/{dto.Hash}", serverDto);
            }
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new RemoteServerException($"Code {response.StatusCode}: {response.ReasonPhrase}. Response body: {responseBody}");
        }
        catch (Exception ex)
        {
            _logger.Write($"[OFFICIAL_ADAPTER] Failed to upload history {dto.Type}/{dto.Hash}: {ex.Message}");
            throw;
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

    public async Task SetCurrentProfile(ProfileDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = new Uri(_httpClient.BaseAddress!, "SyncClipboard.json");
            using var response = await _httpClient.PutAsJsonAsync(url, dto, JsonSerializerOptions.Web, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new RemoteHistoryNotFoundException($"Profile not found in history: {dto.Type}/{dto.Hash}");
            }

            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.Write($"[OFFICIAL_ADAPTER] Failed to set current profile from history {dto.Type}/{dto.Hash}: {ex.Message}");
            throw;
        }
    }

    public async Task<ProfileDto?> GetCurrentProfileAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = new Uri(_httpClient.BaseAddress!, "SyncClipboard.json");
            using var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ProfileDto>(JsonSerializerOptions.Web, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Write($"[OFFICIAL_ADAPTER] Failed to get current profile: {ex.Message}");
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
