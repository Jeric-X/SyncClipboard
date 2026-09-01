using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SyncClipboard.Server.Core.Services;
using SyncClipboard.Server.Core.Services.PushDevices;
using SyncClipboard.Server.Core.Utilities.History;

namespace SyncClipboard.Test;

[TestClass]
public class PushDeviceRegistryTests
{
    [TestMethod]
    public async Task UpsertAsync_ReplacesRegistrationForSameDevice()
    {
        await using var harness = await RegistryHarness.Create();
        var deviceId = Guid.NewGuid().ToString("D");

        await harness.Registry.UpsertAsync(
            deviceId, "android", "fcm", "old-token", "1.0.0", CancellationToken.None);
        await harness.Registry.UpsertAsync(
            deviceId, "android", "fcm", "new-token", "2.0.0", CancellationToken.None);

        var registrations = await harness.Registry.GetByProviderAsync("fcm", CancellationToken.None);
        Assert.HasCount(1, registrations);
        var registration = registrations[0];
        Assert.AreEqual(deviceId, registration.DeviceId);
        Assert.AreEqual("android", registration.Platform);
        Assert.AreEqual("fcm", registration.Provider);
        Assert.AreEqual("new-token", registration.PushToken);
        Assert.AreEqual("2.0.0", registration.AppVersion);
        Assert.IsTrue(registration.LastUpdated <= DateTimeOffset.UtcNow);
    }

    [TestMethod]
    public async Task RemoveAsync_IsIdempotent()
    {
        await using var harness = await RegistryHarness.Create();
        var deviceId = Guid.NewGuid().ToString("D");
        await harness.Registry.UpsertAsync(
            deviceId, "android", "fcm", "push-token", null, CancellationToken.None);

        Assert.IsTrue(await harness.Registry.RemoveAsync(deviceId, CancellationToken.None));
        Assert.IsFalse(await harness.Registry.RemoveAsync(deviceId, CancellationToken.None));
    }

    private sealed class RegistryHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly HistoryDbContext _dbContext;

        private RegistryHarness(
            SqliteConnection connection,
            HistoryDbContext dbContext,
            PushDeviceRegistry registry)
        {
            _connection = connection;
            _dbContext = dbContext;
            Registry = registry;
        }

        public PushDeviceRegistry Registry { get; }

        public static async Task<RegistryHarness> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<HistoryDbContext>()
                .UseSqlite(connection)
                .Options;
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(value => value.ContentRootPath).Returns(Path.GetTempPath());
            var dbContext = new HistoryDbContext(options, new ServerEnvProvider(environment.Object));
            await dbContext.Database.MigrateAsync();
            var registry = new PushDeviceRegistry(
                dbContext, NullLogger<PushDeviceRegistry>.Instance);
            return new RegistryHarness(connection, dbContext, registry);
        }

        public async ValueTask DisposeAsync()
        {
            await _dbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
