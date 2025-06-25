using Quartz;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using System.Globalization;

namespace SyncClipboard.Core.Utilities.Job;

public class AppdataFileDeleteJob(ConfigManager configManager) : IJob
{
    private readonly ConfigManager _configManager = configManager;

    public Task Execute(IJobExecutionContext context)
    {
        return Task.Run(() => PlannedTask(_configManager));
    }

    private static void PlannedTask(ConfigManager configManager)
    {
        try
        {
            var config = configManager.GetConfig<ProgramConfig>();
            if (config.TempFileRemainDays != 0)
            {
                var tempFolders = new DirectoryInfo(Env.AppDataFileFolder).EnumerateDirectories("????????");
                foreach (var dirs in tempFolders)
                {
                    var isTime = DateTime.TryParseExact(dirs.Name, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var createTime);
                    if (!isTime || ((DateTime.Today - createTime) > TimeSpan.FromDays(config.TempFileRemainDays)))
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
                DeleteOutDateFile(logFiles, "yyyyMMdd", TimeSpan.FromDays(config.LogRemainDays));
                DeleteOutDateFile(dumpFiles, "yyyy-MM-dd HH-mm-ss", TimeSpan.FromDays(config.LogRemainDays));
            }
        }
        catch { }
    }

    private static void DeleteOutDateFile(IEnumerable<FileSystemInfo> files, string format, TimeSpan time)
    {
        foreach (var file in files)
        {
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
}