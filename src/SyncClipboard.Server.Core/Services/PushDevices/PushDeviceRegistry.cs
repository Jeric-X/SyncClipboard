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
        var registration = await dbContext.PushDeviceRegistrations
            .SingleOrDefaultAsync(value => value.DeviceId == deviceId, cancellationToken);
        if (registration is null)
        {
            logger.LogDebug("Push registration already absent for device {DeviceId}", deviceId);
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
