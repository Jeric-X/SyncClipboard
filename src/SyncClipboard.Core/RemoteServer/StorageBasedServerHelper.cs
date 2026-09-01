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

internal class StorageBasedServerHelper(IServiceProvider sp, IStorageBasedServerAdapter serverAdapter)
{
    private readonly IStorageBasedServerAdapter _serverAdapter = serverAdapter;
    private readonly ILogger _logger = sp.GetRequiredService<ILogger>();
    private readonly ITrayIcon _trayIcon = sp.GetRequiredService<ITrayIcon>();
    private readonly IProfileEnv _profileEnv = sp.GetRequiredService<IProfileEnv>();

    public event Action? ExceptionOccurred;

    public async void InitializeAsync()
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
        var remoteProfileWithoutHash = await GetRemoteFileProfileWithoutHash(profile, cancellationToken);
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
            await BackfillRemoteProfileHash(remoteProfileWithoutHash, profile, cancellationToken);
            _logger.Write($"[PULL] Downloaded {fileName} to {dataPath}");
            _trayIcon.SetStatusString(ServerConstants.StatusName, "Running.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            const string timeoutMessage = "File download timed out";
            SetErrorStatus(timeoutMessage);
            throw new ProfileDataDownloadException(timeoutMessage);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            SetErrorStatus("Failed to download profile data", ex);
            throw new ProfileDataDownloadException("Failed to download profile data", ex);
        }
    }

    private static async Task<ProfileDto?> GetRemoteFileProfileWithoutHash(
        Profile profile,
        CancellationToken cancellationToken)
    {
        if (profile is not FileProfile ||
            !string.IsNullOrEmpty(await profile.GetHash(cancellationToken)))
        {
            return null;
        }

        return await profile.ToProfileDto(cancellationToken);
    }

    private async Task BackfillRemoteProfileHash(
        ProfileDto? originalProfile,
        Profile downloadedProfile,
        CancellationToken cancellationToken)
    {
        if (originalProfile is null)
        {
            return;
        }

        try
        {
            var currentSnapshot = await _serverAdapter.GetProfileSnapshotAsync(cancellationToken);
            if (currentSnapshot is null ||
                !CanBackfillRemoteProfileHash(originalProfile, currentSnapshot.Profile))
            {
                _logger.Write("[PULL] Remote profile does not meet hash backfill preconditions, skipped metadata update.");
                return;
            }

            var calculatedHash = await downloadedProfile.GetHash(cancellationToken);
            if (string.IsNullOrEmpty(calculatedHash))
            {
                return;
            }

            var updated = await _serverAdapter.TrySetProfileAsync(
                currentSnapshot.Profile with { Hash = calculatedHash },
                currentSnapshot.Version,
                cancellationToken);
            if (!updated)
            {
                _logger.Write("[PULL] Remote profile changed during hash backfill, skipped metadata update.");
                return;
            }

            _logger.Write($"[PULL] Backfilled remote profile hash: {calculatedHash}");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Write($"[PULL] Failed to backfill remote profile hash: {ex}");
        }
    }

    private static bool CanBackfillRemoteProfileHash(ProfileDto originalProfile, ProfileDto? currentProfile)
    {
        return currentProfile is not null &&
            string.IsNullOrEmpty(currentProfile.Hash) &&
            currentProfile.Type == originalProfile.Type &&
            string.Equals(currentProfile.Text, originalProfile.Text, StringComparison.Ordinal) &&
            currentProfile.HasData == originalProfile.HasData &&
            string.Equals(currentProfile.DataName, originalProfile.DataName, StringComparison.Ordinal) &&
            currentProfile.Size == originalProfile.Size;
    }

    public void SetErrorStatus(string message, Exception? innerException = null)
    {
        var statusMessage = $"Server Error: {message}";
        if (innerException != null)
        {
            statusMessage = $"{message}\n{innerException.GetType()}: {innerException.Message}";
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
                return new TextProfile("");
            }

            _trayIcon.SetStatusString(ServerConstants.StatusName, "Running.");
            return Profile.Create(profileDto);
        }
        catch (Exception ex) when (
            ex is JsonException ||
            ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound } ||
            ex is ArgumentException)
        {
            return new TextProfile("");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ThrowServerException("Failed to get remote profile", ex);
            return null!;
        }
    }

    public async Task SetProfileAsync(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await _serverAdapter.CleanupTempFilesAsync(cancellationToken);
            await UploadProfileDataAsync(profile, progress, cancellationToken);
            var profileDto = await profile.ToProfileDto(cancellationToken);
            await _serverAdapter.SetProfileAsync(profileDto, cancellationToken);

            _logger.Write($"[PUSH] Profile metadata updated: {JsonSerializer.Serialize(profileDto)}");
            _trayIcon.SetStatusString(ServerConstants.StatusName, "Running.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            ThrowServerException("Failed to set remote profile", ex);
        }
    }

    private async Task UploadProfileDataAsync(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
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
            await _serverAdapter.UploadFileAsync(fileName, localDataPath, progress, cancellationToken);
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
