using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Shared.Profiles;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;

namespace SyncClipboard.Core.RemoteServer;

internal class StorageBasedServerHelper
{
    private readonly IStorageBasedServerAdapter _serverAdapter;
    private readonly ILogger _logger;
    private readonly ITrayIcon _trayIcon;
    private readonly IProfileEnv _profileEnv;

    public event Action? ExceptionOccurred;

    public StorageBasedServerHelper(IServiceProvider sp, IStorageBasedServerAdapter serverAdapter)
    {
        _serverAdapter = serverAdapter;
        _logger = sp.GetRequiredService<ILogger>();
        _trayIcon = sp.GetRequiredService<ITrayIcon>();
        _profileEnv = sp.GetRequiredService<IProfileEnv>();

        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        try
        {
            await _serverAdapter.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.Write("StorageBasedServerHelper", $"failed to initialize: {ex.Message}");
        }
    }

    public async Task DownloadProfileDataAsync(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var persistentDir = _profileEnv.GetPersistentDir();
        var dataPath = await profile.NeedsTransferData(persistentDir, cancellationToken);
        if (dataPath is null)
        {
            return;
        }

        try
        {
            var fileName = Path.GetFileName(dataPath);
            await _serverAdapter.DownloadFileAsync(fileName, dataPath, progress, cancellationToken);
            await profile.SetAndMoveTransferData(persistentDir, dataPath, cancellationToken);
            _logger.Write($"[PULL] Downloaded {fileName} to {dataPath}");
            _trayIcon.SetStatusString(ServerConstants.StatusName, "Running.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ThrowServerException("Failed to download profile data", ex);
        }
    }

    public void SetErrorStatus(string message, Exception? innerException = null)
    {
        var statusMessage = $"Server Error: {message}";
        if (innerException != null)
        {
            statusMessage = $"{message}\n{innerException.Message}";
        }
        _logger.Write(statusMessage);
        _trayIcon.SetStatusString(ServerConstants.StatusName, statusMessage);
    }

    [DoesNotReturn]
    public void ThrowServerException(string message, Exception? innerException = null)
    {
        SetErrorStatus(message, innerException);
        ExceptionOccurred?.Invoke();
        throw new RemoteServerException(message, innerException);
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

            _trayIcon.SetStatusString(ServerConstants.StatusName, "Running.");
            return ClipboardProfileDTO.CreateProfile(profileDto);
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

    public async Task SetProfileAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            await _serverAdapter.CleanupTempFilesAsync(cancellationToken);
            await UploadProfileDataAsync(profile, cancellationToken);
            await _serverAdapter.SetProfileAsync(await profile.ToDto(cancellationToken), cancellationToken);

            _logger.Write($"[PUSH] Profile metadata updated: {JsonSerializer.Serialize(await profile.ToDto(cancellationToken))}");
            _trayIcon.SetStatusString(ServerConstants.StatusName, "Running.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ThrowServerException("Failed to set remote profile", ex);
        }
    }

    private async Task UploadProfileDataAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        var localDataPath = await profile.PrepareDataWithCache(cancellationToken);
        if (localDataPath is null)
        {
            return;
        }

        try
        {
            if (!File.Exists(localDataPath))
            {
                throw new FileNotFoundException($"Local data file not found: {localDataPath}");
            }

            var fileName = Path.GetFileName(localDataPath);
            await _serverAdapter.UploadFileAsync(fileName, localDataPath, cancellationToken);
            _logger.Write($"[PUSH] Upload completed for {fileName}");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ThrowServerException("Failed to upload profile data", ex);
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
}
