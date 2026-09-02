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

internal class StorageBasedServerHelper(IServiceProvider sp, IServerAdapter serverAdapter)
{
    private readonly IServerAdapter _serverAdapter = serverAdapter;
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
        var shouldFillBack = await IsProfileDtoMetadataIncompleteAsync(profile, cancellationToken);
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
            if (shouldFillBack)
            {
                await FillBackRemoteProfile(profile, cancellationToken);
            }
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

    private static async Task<bool> IsProfileDtoMetadataIncompleteAsync(
        Profile profile,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrEmpty(await profile.GetHash(cancellationToken)) ||
            !profile.HasKnownSize;
    }

    private async Task FillBackRemoteProfile(
        Profile downloadedProfile,
        CancellationToken cancellationToken)
    {
        if (_serverAdapter is not IStorageBasedServerAdapter storageBasedServerAdapter)
        {
            return;
        }

        try
        {
            var downloadedProfileDto = await downloadedProfile.ToProfileDto(cancellationToken);
            if (string.IsNullOrEmpty(downloadedProfileDto.Hash))
            {
                return;
            }

            var currentSnapshot = await storageBasedServerAdapter.GetProfileSnapshotAsync(cancellationToken);
            if (currentSnapshot is null ||
                !ShouldFillBack(downloadedProfileDto, currentSnapshot.Profile))
            {
                _logger.Write("[PULL] Remote profile does not meet metadata fill-back preconditions, skipped metadata update.");
                return;
            }

            var updatedProfile = currentSnapshot.Profile with
            {
                Hash = downloadedProfileDto.Hash,
                Size = downloadedProfileDto.Size,
            };
            if (string.IsNullOrWhiteSpace(currentSnapshot.Version))
            {
                await _serverAdapter.SetProfileAsync(updatedProfile, cancellationToken);
                _logger.Write($"[PULL] Filled back remote profile metadata without version precondition: {downloadedProfileDto.Hash}");
                return;
            }

            var updated = await storageBasedServerAdapter.TrySetProfileAsync(
                updatedProfile,
                currentSnapshot.Version,
                cancellationToken);
            if (!updated)
            {
                _logger.Write("[PULL] Remote profile changed during hash backfill, skipped metadata update.");
                return;
            }

            _logger.Write($"[PULL] Filled back remote profile metadata: {downloadedProfileDto.Hash}");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Write($"[PULL] Failed to backfill remote profile hash: {ex}");
        }
    }

    private static bool ShouldFillBack(ProfileDto downloadedProfile, ProfileDto currentProfile)
    {
        if (!string.IsNullOrEmpty(currentProfile.Hash) && currentProfile.Size is not null)
        {
            return false;
        }

        if (!MatchesKnownProfileMetadata(downloadedProfile, currentProfile))
        {
            return false;
        }

        if (!MatchesProfileIdentity(downloadedProfile, currentProfile))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesKnownProfileMetadata(ProfileDto downloadedProfile, ProfileDto currentProfile)
    {
        if (!string.IsNullOrEmpty(currentProfile.Hash))
        {
            if (!string.Equals(currentProfile.Hash, downloadedProfile.Hash, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (currentProfile.Size is not null)
        {
            if (currentProfile.Size != downloadedProfile.Size)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesProfileIdentity(ProfileDto downloadedProfile, ProfileDto currentProfile)
    {
        if (currentProfile.Type != downloadedProfile.Type)
        {
            if (currentProfile.Type != ProfileType.File)
            {
                return false;
            }

            if (downloadedProfile.Type != ProfileType.Image)
            {
                return false;
            }
        }

        if (!string.Equals(currentProfile.Text, downloadedProfile.Text, StringComparison.Ordinal))
        {
            return false;
        }

        if (currentProfile.HasData != downloadedProfile.HasData)
        {
            return false;
        }

        if (!string.Equals(currentProfile.DataName, downloadedProfile.DataName, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
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
