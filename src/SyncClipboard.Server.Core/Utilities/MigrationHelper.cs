using Microsoft.EntityFrameworkCore;
using SyncClipboard.Server.Core.Utilities.History;
namespace SyncClipboard.Server.Core.Utilities;

public static class MigrationHelper
{
    public static void ApplyMigrations(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HistoryDbContext>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("MigrationHelper");

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

        db.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully.");
    }

    public static void EnsureDBMigrations(IServiceProvider services, IHostApplicationLifetime lifetime)
    {
        var cts = new CancellationTokenSource();
        lifetime.ApplicationStopping.Register(cts.Cancel);

        // Resolve logger from a scope so we can use DI logger
        using var scope = services.CreateScope();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("MigrationHelper");

        var bgTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                logger.LogInformation("minrating");
                try
                {
                    await Task.Delay(2000, cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, cts.Token);

        bgTask.ContinueWith(t =>
        {
            logger?.LogError(t.Exception, "Heartbeat task faulted");
        }, TaskContinuationOptions.OnlyOnFaulted);

        try
        {
            ApplyMigrations(services);
            cts.Cancel();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to apply migrations");
            Environment.Exit(1);
        }
    }
}
