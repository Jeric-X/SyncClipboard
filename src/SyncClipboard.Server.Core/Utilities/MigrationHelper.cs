using Microsoft.EntityFrameworkCore;
using SyncClipboard.Server.Core.Utilities.History;
namespace SyncClipboard.Server.Core.Utilities;

public static class MigrationHelper
{
    public static void ApplyMigrations(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HistoryDbContext>();

        var clearLock = Environment.GetEnvironmentVariable("CLEAR_SQLITE_LOCK");
        if (!string.IsNullOrEmpty(clearLock) && clearLock.Equals("true", StringComparison.OrdinalIgnoreCase) && db.Database.IsSqlite())
        {
            try
            {
                db.Database.ExecuteSqlRaw("DELETE FROM __EFMigrationsLock;");
                logger.LogInformation("Cleared __EFMigrationsLock table as requested.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to clear __EFMigrationsLock");
            }
        }
        var pendingMigrations = db.Database.GetPendingMigrations();
        if (pendingMigrations.Any())
        {
            db.Database.Migrate();
            logger.LogInformation("Database migrations applied successfully.");
        }
        else
        {
            logger.LogInformation("Database is up to date. No migrations were applied.");
        }
    }

    public static void EnsureDBMigrations(IServiceProvider services, IHostApplicationLifetime lifetime)
    {
        var cts = new CancellationTokenSource();
        lifetime.ApplicationStopping.Register(cts.Cancel);

        // Resolve logger from a scope so we can use DI logger
        using var scope = services.CreateScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("MigrationHelper");

        var bgTask = StartLogHeartbeatAsync(logger, cts.Token);

        try
        {
            ApplyMigrations(services, logger);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to apply database migrations");
            Environment.Exit(1);
        }
        finally
        {
            cts.Cancel();
        }
    }

    private static Task StartLogHeartbeatAsync(ILogger logger, CancellationToken token)
    {
        var bgTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                logger.LogInformation("Migrating");
                try
                {
                    await Task.Delay(2000, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);

        bgTask.ContinueWith(t =>
        {
            logger?.LogError(t.Exception, "Heartbeat task faulted");
        }, TaskContinuationOptions.OnlyOnFaulted);
        return bgTask;
    }
}
