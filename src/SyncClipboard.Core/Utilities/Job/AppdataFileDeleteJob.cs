using Quartz;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Updater;
using System.Globalization;

namespace SyncClipboard.Core.Utilities.Job;

public class AppdataFileDeleteJob(ConfigManager configManager) : IJob
{
    private readonly ConfigManager _configManager = configManager;

    public Task Execute(IJobExecutionContext context)
    {
        return Task.Run(
            () => PlannedTask(_configManager, context.CancellationToken),
            context.CancellationToken);
    }

    private static void PlannedTask(ConfigManager configManager, CancellationToken token)
    {
        try
        {
            DeleteUpdatePackageFiles(token);

            var config = configManager.GetConfig<ProgramConfig>();
            if (config.TempFileRemainDays != 0)
            {
                var tempFolders = new DirectoryInfo(Env.AppDataFileFolder).EnumerateDirectories("????????");
                foreach (var dirs in tempFolders)
                {
                    token.ThrowIfCancellationRequested();
                    var isTime = DateTime.TryParseExact(dirs.Name, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var createTime);
                    if (isTime && ((DateTime.Today - createTime) > TimeSpan.FromDays(config.TempFileRemainDays)))
                    {
                        dirs.Delete(true);
                    }
                }
            }

            var logFolder = new DirectoryInfo(Env.LogFolder);
            if (logFolder.Exists && config.LogRemainDays != 0)
            {
                var logFiles = logFolder.EnumerateFiles("????????.txt");
                var dumpFiles = logFolder.EnumerateFiles("????-??-?? ??-??-??.dmp");
                DeleteOutDateFile(logFiles, "yyyyMMdd", TimeSpan.FromDays(config.LogRemainDays), token);
                DeleteOutDateFile(dumpFiles, "yyyy-MM-dd HH-mm-ss", TimeSpan.FromDays(config.LogRemainDays), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch { }
    }

    private static void DeleteOutDateFile(
        IEnumerable<FileSystemInfo> files,
        string format,
        TimeSpan time,
        CancellationToken token)
    {
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var createTime = DateTime.ParseExact(
                Path.GetFileNameWithoutExtension(file.Name),
                format,
                CultureInfo.InvariantCulture
            );
            if ((DateTime.Today - createTime) > time)
            {
                file.Delete();
            }
        }
    }

    private static void DeleteUpdatePackageFiles(CancellationToken token)
    {
        var updateFolder = new DirectoryInfo(Env.UpdateFolder);
        if (!updateFolder.Exists)
        {
            return;
        }
        updateFolder.EnumerateFiles().ForEach(file =>
        {
            token.ThrowIfCancellationRequested();
            file.Delete();
        });

        List<KeyValuePair<DirectoryInfo, AppVersion>> updateDirs = [];
        updateFolder.EnumerateDirectories().ForEach(dir =>
        {
            token.ThrowIfCancellationRequested();
            if (AppVersion.TryParse(dir.Name, out var appVersion))
            {
                updateDirs.Add(new KeyValuePair<DirectoryInfo, AppVersion>(dir, appVersion));
            }
            else
            {
                dir.Delete(true);
            }
        });
        updateDirs.OrderByDescending(x => x.Value).Skip(2).ForEach(x =>
        {
            token.ThrowIfCancellationRequested();
            x.Key.Delete(true);
        });
    }
}
