using Quartz;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Updater;
using System.Globalization;

namespace SyncClipboard.Core.Utilities.Job;

public class AppdataFileDeleteJob(ConfigManager configManager, ILogger logger) : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        return Task.Run(
            () => PlannedTask(configManager.GetConfig<ProgramConfig>(), Env.AppDataDirectory, logger, context.CancellationToken),
            context.CancellationToken);
    }

    internal static void PlannedTask(ProgramConfig config, string appDataDirectory, ILogger logger, CancellationToken token)
    {
        var updateFolder = new DirectoryInfo(Path.Combine(appDataDirectory, "update"));
        TryCleanup(() => DeleteUpdatePackageFiles(updateFolder, logger, token), "updates", updateFolder.FullName, logger, token);

        if (config.TempFileRemainDays != 0)
        {
            var tempFolder = new DirectoryInfo(Path.Combine(appDataDirectory, "file"));
            TryCleanup(() =>
            {
                if (tempFolder.Exists)
                {
                    DeleteOutDateFile(tempFolder.EnumerateDirectories("????????"), "yyyyMMdd",
                        TimeSpan.FromDays(config.TempFileRemainDays), "cache", logger, token);
                }
            }, "cache", tempFolder.FullName, logger, token);
        }

        if (config.LogRemainDays != 0)
        {
            var logFolder = new DirectoryInfo(Path.Combine(appDataDirectory, "log"));
            var retention = TimeSpan.FromDays(config.LogRemainDays);
            TryCleanup(() =>
            {
                if (logFolder.Exists)
                {
                    DeleteOutDateFile(logFolder.EnumerateFiles("????????.txt"), "yyyyMMdd", retention, "logs", logger, token);
                }
            }, "logs", logFolder.FullName, logger, token);
            TryCleanup(() =>
            {
                if (logFolder.Exists)
                {
                    DeleteOutDateFile(logFolder.EnumerateFiles("????-??-?? ??-??-??.dmp"),
                        "yyyy-MM-dd HH-mm-ss", retention, "dumps", logger, token);
                }
            }, "dumps", logFolder.FullName, logger, token);
        }
    }

    private static void TryCleanup(Action action, string stage, string path, ILogger logger, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            action();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Write(nameof(AppdataFileDeleteJob), $"Failed to clean {stage} at '{path}': {ex}");
        }
        token.ThrowIfCancellationRequested();
    }

    private static void DeleteEntry(FileSystemInfo entry, string stage, ILogger logger, CancellationToken token)
    {
        TryCleanup(() =>
        {
            if (entry is DirectoryInfo directory)
            {
                directory.Delete(true);
            }
            else
            {
                entry.Delete();
            }
        }, stage, entry.FullName, logger, token);
    }

    private static void DeleteOutDateFile(
        IEnumerable<FileSystemInfo> files,
        string format,
        TimeSpan time,
        string stage,
        ILogger logger,
        CancellationToken token)
    {
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), format,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var createTime)
                && (DateTime.Today - createTime) > time)
            {
                DeleteEntry(file, stage, logger, token);
            }
        }
    }

    private static void DeleteUpdatePackageFiles(DirectoryInfo updateFolder, ILogger logger, CancellationToken token)
    {
        if (!updateFolder.Exists)
        {
            return;
        }

        TryCleanup(() =>
        {
            foreach (var file in updateFolder.EnumerateFiles())
            {
                DeleteEntry(file, "updates", logger, token);
            }
        }, "updates", updateFolder.FullName, logger, token);

        List<KeyValuePair<DirectoryInfo, AppVersion>> updateDirs = [];
        foreach (var dir in updateFolder.EnumerateDirectories())
        {
            token.ThrowIfCancellationRequested();
            if (AppVersion.TryParse(dir.Name, out var appVersion))
            {
                updateDirs.Add(new KeyValuePair<DirectoryInfo, AppVersion>(dir, appVersion));
            }
            else
            {
                DeleteEntry(dir, "updates", logger, token);
            }
        }
        foreach (var entry in updateDirs.OrderByDescending(x => x.Value).Skip(2))
        {
            DeleteEntry(entry.Key, "updates", logger, token);
        }
    }
}
