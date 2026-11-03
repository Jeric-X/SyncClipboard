using Microsoft.AspNetCore.SignalR.Client;
using NativeNotification.Interface;
using SyncClipboard.Core.I18n;
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
using System.Security.Cryptography;

namespace SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;

public sealed class OfficialAdapter(
    ILogger logger,
    IAppConfig appConfig,
    INotificationManager? notifications = null)
    : IServerAdapter<OfficialConfig>, IOfficialServerAdapter, IOfficialSyncServer, IDisposable
{
    private const int DownloadBufferSize = 102400;
    private readonly ILogger _logger = logger;
    private readonly IAppConfig _appConfig = appConfig;
    private readonly WebDavAdapter _webDavAdapter = new(logger, appConfig);
    private readonly Lock _hubLock = new();
    private readonly Lock _httpClientLock = new();
    private HubConnection? _hubConnection;
    private OfficialConfig _officialConfig = new OfficialConfig();
    private HttpClient _httpClient = new HttpClient();
    private IWebProxy _proxy = new WebProxy();

    public event Action<ProfileDto>? ProfileDtoChanged;
    public event Action<HistoryRecordDto>? HistoryChanged;
    public event Action<Exception?>? ServerDisconnected;
    public event Action? ServerConnected;

    public void SetConfig(OfficialConfig config, SyncConfig syncConfig)
    {
        if (!Equals(_officialConfig, config))
        {
            _capabilities = null;
            Interlocked.Increment(ref _configurationVersion);
            _historyTagsNotified = 0;
        }
        _officialConfig = config;
        _webDavAdapter.SetConfig(new WebDavConfig
        {
            RemoteURL = config.RemoteURL,
            UserName = config.UserName,
            Password = config.Password,
            DeletePreviousFilesOnPush = config.DeletePreviousFilesOnPush
        }, syncConfig);
    }

    public void SetProxy(IWebProxy proxy)
    {
        _proxy = proxy;
        _webDavAdapter.SetProxy(proxy);
    }

    public void ApplyConfig()
    {
        try
        {
            _webDavAdapter.ApplyConfig();
            ReCreateHttpClient();
            ReConnectSignalR();
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
            if (string.IsNullOrWhiteSpace(signalRUrl))
                return;
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(new Uri(signalRUrl), config =>
                {
                    var base64 = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_officialConfig.UserName}:{_officialConfig.Password}"));
                    config.Headers.Add("Authorization", "Basic " + base64);
                    config.Proxy = _proxy;
                })
                .Build();
        }

        _hubConnection.On<ProfileDto>(nameof(ISyncClipboardClient.RemoteProfileChanged), profile =>
        {
            ProfileDtoChanged?.Invoke(profile);
        });

        StartSignalRConnectiron(_hubConnection);
    }

    private async void StartSignalRConnectiron(HubConnection hubConnection)
    {
        try
        {
            var capabilities = await CheckHistoryCapabilitiesAsync(forceRefresh: true, CancellationToken.None);
            lock (_hubLock)
            {
                if (_disposed || !ReferenceEquals(_hubConnection, hubConnection))
                    return;
                // Bind the confirmed capabilities to this connection; receiving an event must not depend on another HTTP request.
                hubConnection.On<HistoryRecordDto>(nameof(ISyncClipboardClient.RemoteHistoryChanged), historyRecord =>
                    HistoryChanged?.Invoke(ReadHistoryTags(historyRecord, capabilities.HistoryTags)!));
            }
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
        HubConnection? old;
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

        await CheckHistoryCapabilitiesAsync(forceRefresh: false, cancellationToken);
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
        _disposed = true;
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

    private sealed record CapabilityCache(HttpClient Client, int ConfigurationVersion, DateTime CheckedAt, ServerCapabilitiesDto Value);

    private CapabilityCache? _capabilities;
    private readonly SemaphoreSlim _capabilitiesLock = new(1, 1);
    private int _configurationVersion;
    private int _historyTagsNotified;
    private bool _disposed;

    private async Task<ServerCapabilitiesDto> CheckHistoryCapabilitiesAsync(bool forceRefresh, CancellationToken token)
    {
        await _capabilitiesLock.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var client = _httpClient;
            var configurationVersion = Volatile.Read(ref _configurationVersion);
            var cached = _capabilities;
            if (forceRefresh || cached is null || !ReferenceEquals(cached.Client, client)
                || cached.ConfigurationVersion != configurationVersion
                || DateTime.UtcNow - cached.CheckedAt > TimeSpan.FromMinutes(1))
            {
                var capabilities = await ReadCapabilitiesAsync(client, token);
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!ReferenceEquals(client, _httpClient) || configurationVersion != Volatile.Read(ref _configurationVersion))
                    throw new OperationCanceledException("Server changed during capability check.");
                cached = new(client, configurationVersion, DateTime.UtcNow, capabilities);
                _capabilities = cached;
            }
            if (!cached.Value.HistorySync)
                throw new InvalidOperationException("Server does not support history sync. Please upgrade your server.");
            if (!cached.Value.HistoryTags && Interlocked.Exchange(ref _historyTagsNotified, 1) == 0)
                notifications?.Show(Strings.HistoryTagsSyncTitle, Strings.HistoryTagsUpgrade);
            return cached.Value;
        }
        finally
        {
            _capabilitiesLock.Release();
        }
    }

    private static async Task<ServerCapabilitiesDto> ReadCapabilitiesAsync(HttpClient client, CancellationToken token)
    {
        using var response = await client.GetAsync("api/capabilities", token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var version = await ReadVersionAsync(client, token);
            if (!AppVersion.TryParse(version, out var serverVersion) || !AppVersion.TryParse(Env.RequestServerVersion, out var requiredVersion))
                throw new InvalidDataException("Invalid server version response.");
            if (serverVersion < requiredVersion)
                throw new InvalidOperationException($"Server version ({version}) is lower than requested version ({Env.RequestServerVersion}). Please upgrade your server.");
            return new() { HistorySync = true, HistoryTags = false };
        }
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("historySync", out var sync) || sync.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !root.TryGetProperty("historyTags", out var tags) || tags.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Invalid server capabilities response.");
        return new() { HistorySync = sync.GetBoolean(), HistoryTags = tags.GetBoolean() };
    }

    private static HistoryRecordDto? ReadHistoryTags(HistoryRecordDto? record, bool supportsHistoryTags)
    {
        if (record is not null && !supportsHistoryTags)
            record.Tags = null;
        return record;
    }

    private static HistoryRecordUpdateDto? ReadHistoryTags(HistoryRecordUpdateDto? record, bool supportsHistoryTags)
    {
        if (record is not null && !supportsHistoryTags)
            record.Tags = null;
        return record;
    }

    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) =>
        ReadVersionAsync(_httpClient, cancellationToken);

    private static async Task<string> ReadVersionAsync(HttpClient client, CancellationToken token)
    {
        using var response = await client.GetAsync("api/version", token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(token);
    }

    public async Task<IEnumerable<HistoryRecordDto>> GetHistoryAsync(
        int page = 1, DateTimeOffset? before = null, DateTimeOffset? after = null, DateTimeOffset? modifiedAfter = null,
        ProfileTypeFilter types = ProfileTypeFilter.All, string? searchText = null, bool? starred = null,
        bool sortByLastAccessed = false, CancellationToken token = default)
    {
        var capabilities = await CheckHistoryCapabilitiesAsync(forceRefresh: false, token);
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

            using var response = await _httpClient.PostAsync(url, content, token);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(token);
            var records = await JsonSerializer.DeserializeAsync<List<HistoryRecordDto>>(
                stream, JsonSerializerOptions.Web, token);

            foreach (var record in records ?? [])
                ReadHistoryTags(record, capabilities.HistoryTags);
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
        var capabilities = await CheckHistoryCapabilitiesAsync(forceRefresh: false, cancellationToken);
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
            return ReadHistoryTags(dto, capabilities.HistoryTags);
        }
        catch (Exception ex)
        {
            _logger.Write($"[OFFICIAL_ADAPTER] Failed to get history by profileId {profileId}: {ex.Message}");
            throw;
        }
    }

    public async Task UpdateHistoryAsync(ProfileType type, string hash, HistoryRecordUpdateDto dto, CancellationToken cancellationToken = default)
    {
        var capabilities = await CheckHistoryCapabilitiesAsync(forceRefresh: false, cancellationToken);
        try
        {
            var url = new Uri(_httpClient.BaseAddress!, $"api/history/{type}/{hash}");
            var payload = new HistoryRecordUpdateDto
            {
                Tags = capabilities.HistoryTags ? dto.Tags : null,
                Starred = dto.Starred,
                Pinned = dto.Pinned,
                IsDelete = dto.IsDelete,
                Version = dto.Version,
                LastModified = dto.LastModified,
                LastAccessed = dto.LastAccessed
            };
            using var response = await _httpClient.PatchAsJsonAsync(url, payload, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return; // 成功不再返回 payload
            }
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var serverDto = await response.Content.ReadFromJsonAsync<HistoryRecordUpdateDto>(cancellationToken: cancellationToken);
                throw new RemoteHistoryConflictException($"History update conflict {type}/{hash}", ReadHistoryTags(serverDto, capabilities.HistoryTags));
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

    public async Task<HistoryRecordDto> UploadHistoryAsync(
        HistoryRecordDto dto, FileHashInfo? file, IProgress<HttpDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var capabilities = await CheckHistoryCapabilitiesAsync(forceRefresh: false, cancellationToken);
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
                { new StringContent(dto.Size.ToString()), "size" },
                { new StringContent(dto.HasData.ToString()), "hasData" }
            };

            if (capabilities.HistoryTags && dto.Tags is not null)
                content.Add(new StringContent(JsonSerializer.Serialize(dto.Tags)), nameof(HistoryRecordDto.Tags));

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content,
            };

            // 添加文件字段（如果提供）
            if (file is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(file.Path);
                var normalizedTransferDataHash = Utility.NormalizeSHA256(file.Hash)
                    ?? throw new ArgumentException(
                        "Transfer data hash is required when uploading history data.", nameof(file));
                var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                HttpContent fileContent = progress is null
                    ? new StreamContent(stream)
                    : new ProgressableStreamContent(stream, progress, cancellationToken);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, "data", Path.GetFileName(file.Path));

                request.Headers.Add(HistoryTransferDataHeaders.TransferDataHash, normalizedTransferDataHash);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureHistoryUploadSucceeded(dto, response, capabilities, cancellationToken);
            return ReadHistoryTags(await response.Content.ReadFromJsonAsync<HistoryRecordDto>(cancellationToken: cancellationToken), capabilities.HistoryTags)
                ?? throw new InvalidDataException("History upload response did not contain a record.");
        }
        catch (Exception ex)
        {
            _logger.Write($"[OFFICIAL_ADAPTER] Failed to upload history {dto.Type}/{dto.Hash}: {ex.Message}");
            throw;
        }
    }

    private static async Task EnsureHistoryUploadSucceeded(
        HistoryRecordDto dto, HttpResponseMessage response, ServerCapabilitiesDto capabilities, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            HistoryRecordUpdateDto? serverDto = null;
            try
            {
                serverDto = await response.Content.ReadFromJsonAsync<HistoryRecordUpdateDto>(
                    cancellationToken: cancellationToken);
            }
            catch
            {
                // Ignore parse errors and fall back to a conflict without server metadata.
            }
            throw new RemoteHistoryConflictException(
                $"History already exists {dto.Type}/{dto.Hash}",
                ReadHistoryTags(serverDto, capabilities.HistoryTags));
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
        {
            throw new RemoteHistoryDataRejectedException(
                $"History data was rejected for {dto.Type}/{dto.Hash}. Code {response.StatusCode}: {responseBody}");
        }
        throw new RemoteServerException(
            $"Code {response.StatusCode}: {response.ReasonPhrase}. Response body: {responseBody}");
    }

    public async Task<string?> DownloadHistoryDataAsync(
        string profileId, string localPath, IProgress<HttpDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await CheckHistoryCapabilitiesAsync(forceRefresh: false, cancellationToken);
        try
        {
            var url = new Uri(_httpClient.BaseAddress!, $"api/history/{HttpUtility.UrlEncode(profileId)}/data");
            using var response = await _httpClient.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await SaveHistoryDataResponseAsync(response, localPath, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Write($"[OFFICIAL_ADAPTER] Failed to download history data for {profileId}: {ex.Message}");
            throw;
        }
    }

    internal static async Task<string?> SaveHistoryDataResponseAsync(
        HttpResponseMessage response, string localPath, IProgress<HttpDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{localPath}.{Guid.NewGuid():N}.download";
        try
        {
            var expectedTransferDataHash = ReadTransferDataHash(response);
            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var downloadProgress = new HttpDownloadProgress
            {
                TotalBytesToReceive = (ulong?)response.Content.Headers.ContentLength,
            };
            progress?.Report(downloadProgress);

            using var incrementalHash = expectedTransferDataHash is null
                ? null
                : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var fileStream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, DownloadBufferSize, useAsync: true))
            {
                var buffer = new byte[DownloadBufferSize];
                int bytesRead;
                while ((bytesRead = await responseStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    incrementalHash?.AppendData(buffer, 0, bytesRead);
                    downloadProgress.BytesReceived += (ulong)bytesRead;
                    progress?.Report(downloadProgress);
                }
            }

            string? actualTransferDataHash = null;
            if (incrementalHash is not null)
            {
                actualTransferDataHash = Convert.ToHexString(incrementalHash.GetHashAndReset());
                if (!Utility.SHA256Same(actualTransferDataHash, expectedTransferDataHash))
                {
                    throw new RemoteHistoryDataRejectedException(
                        $"Downloaded history data hash mismatch. Expected: {expectedTransferDataHash}, Actual: {actualTransferDataHash}.");
                }
            }

            File.Move(temporaryPath, localPath, overwrite: true);
            downloadProgress.End = true;
            progress?.Report(downloadProgress);
            return actualTransferDataHash;
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            { }
        }
    }

    private static string? ReadTransferDataHash(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(HistoryTransferDataHeaders.TransferDataHash, out var values))
        {
            return null;
        }

        var headerValues = values.ToArray();
        if (headerValues.Length != 1)
        {
            throw new RemoteHistoryDataRejectedException(
                $"{HistoryTransferDataHeaders.TransferDataHash} must contain exactly one value.");
        }

        try
        {
            return Utility.NormalizeSHA256(headerValues[0])
                ?? throw new RemoteHistoryDataRejectedException(
                    $"{HistoryTransferDataHeaders.TransferDataHash} cannot be empty.");
        }
        catch (ArgumentException ex)
        {
            throw new RemoteHistoryDataRejectedException(
                $"{HistoryTransferDataHeaders.TransferDataHash} is not a valid SHA-256 hash: {ex.Message}");
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

            var handler = new HttpClientHandler
            {
                Proxy = _proxy // 显式应用代理
            };
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
