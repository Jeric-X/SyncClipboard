using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Server.Core.Services.PushDevices;

public interface IPushDeviceRegistry
{
    Task UpsertAsync(
        string deviceId,
        string platform,
        string provider,
        string pushToken,
        string? appVersion,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PushDeviceRegistration>> GetByProviderAsync(
        string provider,
        CancellationToken cancellationToken = default);
}
