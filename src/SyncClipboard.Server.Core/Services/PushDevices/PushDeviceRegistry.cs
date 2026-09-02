using Microsoft.EntityFrameworkCore;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Utilities.History;

namespace SyncClipboard.Server.Core.Services.PushDevices;

public class PushDeviceRegistry(
    HistoryDbContext dbContext,
    ILogger<PushDeviceRegistry> logger) : IPushDeviceRegistry
{
    public async Task UpsertAsync(
        string deviceId,
        string platform,
        string provider,
        string pushToken,
        string? appVersion,
        CancellationToken cancellationToken = default)
    {
        var lastUpdated = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PushDeviceRegistrations"
                ("DeviceId", "Platform", "Provider", "PushToken", "AppVersion", "LastUpdated")
            VALUES
                ({deviceId}, {platform}, {provider}, {pushToken}, {appVersion}, {lastUpdated})
            ON CONFLICT ("DeviceId") DO UPDATE SET
                "Platform" = excluded."Platform",
                "Provider" = excluded."Provider",
                "PushToken" = excluded."PushToken",
                "AppVersion" = excluded."AppVersion",
                "LastUpdated" = excluded."LastUpdated";
            """, cancellationToken);

        logger.LogDebug(
            "Upserted push registration for device {DeviceId} using {Provider}",
            deviceId,
            provider);
    }

    public async Task<bool> RemoveAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        return await RemoveInternalAsync(deviceId, pushToken: null, cancellationToken);
    }

    public async Task<bool> RemoveIfTokenMatchesAsync(
        string deviceId,
        string pushToken,
        CancellationToken cancellationToken = default)
    {
        return await RemoveInternalAsync(deviceId, pushToken, cancellationToken);
    }

    private async Task<bool> RemoveInternalAsync(
        string deviceId,
        string? pushToken,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PushDeviceRegistrations
            .Where(value => value.DeviceId == deviceId);
        if (pushToken is not null)
        {
            query = query.Where(value => value.PushToken == pushToken);
        }
        var registration = await query.SingleOrDefaultAsync(cancellationToken);
        if (registration is null)
        {
            logger.LogDebug(
                "Push registration already absent or changed for device {DeviceId}",
                deviceId);
            return false;
        }

        dbContext.PushDeviceRegistrations.Remove(registration);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogDebug("Removed push registration for device {DeviceId}", deviceId);
        return true;
    }

    public async Task<IReadOnlyList<PushDeviceRegistration>> GetByProviderAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        var registrations = await dbContext.PushDeviceRegistrations
            .AsNoTracking()
            .Where(value => value.Provider == provider)
            .ToListAsync(cancellationToken);
        return registrations.Select(value => value.ToRegistration()).ToList();
    }
}
