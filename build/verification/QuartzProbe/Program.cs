using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NativeNotification.Interface;
using Quartz;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.FileCacheManager;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Job;
using SyncClipboard.Core.Utilities.Updater;
using SyncClipboard.Shared.Profiles;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoreLogger = SyncClipboard.Core.Interfaces.ILogger;

if (args.Length != 2) throw new ArgumentException("Expected isolated root and report path.");
var root = Path.GetFullPath(args[0]);
var executable = Path.Combine(root, "probe");
if (!Path.GetFileName(root).StartsWith("syncclipboard-quartz-jobs-", StringComparison.Ordinal)
    || Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory) != executable)
    throw new InvalidOperationException("Only an isolated copy of the probe may run.");
using (var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(executable, "StaticConfig.json"))))
{
    var env = config.RootElement.GetProperty("Env");
    if (!env.GetProperty("PortableAppDataFolder").GetBoolean() || !env.GetProperty("PortableUserConfig").GetBoolean())
        throw new InvalidOperationException("Portable storage must be configured before starting the process.");
}
await Run(root, Path.GetFullPath(args[1]));

[MethodImpl(MethodImplOptions.NoInlining)]
static async Task Run(string root, string reportPath)
{
    var expectedData = Path.Combine(root, "probe", "appdata");
    Check(Env.AppDataDirectory == expectedData, "portable data root");
    var notification = new Mock<INotificationManager>(MockBehavior.Strict);
    var window = new Mock<IMainWindow>(MockBehavior.Strict);
    var http = new Mock<IHttp>(MockBehavior.Strict);
    var logger = new ProbeLogger();
    var config = new ConfigManager(new StaticConfig(notification.Object), notification.Object, new SyncClipboardConfigUpgrader());
    config.SetConfig(new ProgramConfig { TempFileRemainDays = 2, LogRemainDays = 2, CheckUpdateOnStartUp = false });
    config.SetConfig(new HistoryConfig { MaxItemCount = 0, HistoryRetentionMinutes = 1 });
    var runtime = new ConfigBase(Path.Combine(expectedData, "runtime-probe.json"));
    runtime.SetConfig(new RuntimeHistoryConfig { EnableSyncHistory = false });
    var profileEnv = new ProbeProfileEnv();
    var history = new HistoryManager(config, logger, profileEnv, runtime);
    await history.GetHistoryAsync(ProfileTypeFilter.All);
    using var cache = new LocalFileCacheManager(logger);
    var updater = new UpdateChecker(null!, http.Object, logger, null!, notification.Object, window.Object, config, new ConfigBase());
    var dispatcher = new Mock<IThreadDispatcher>(MockBehavior.Strict);
    var dispatches = 0;
    var failDispatch = false;
    dispatcher.Setup(x => x.RunOnMainThreadAsync(It.IsAny<Func<Task>>())).Callback((Func<Task> callback) =>
    {
        Check(ReferenceEquals(callback.Target, updater) && callback.Method.Name == nameof(UpdateChecker.RunAutoUpdateFlow), "update callback identity");
        dispatches++;
        // Deliberately do not invoke the callback: it would enter the real update/UI flow.
    }).Returns(() => failDispatch ? Task.FromException(new InvalidOperationException("Expected dispatcher failure.")) : Task.CompletedTask);

    var collection = new ServiceCollection();
    collection.AddSingleton(config);
    collection.AddSingleton(history);
    collection.AddSingleton(cache);
    collection.AddSingleton<CoreLogger>(logger);
    collection.AddSingleton(updater);
    collection.AddSingleton(dispatcher.Object);
    collection.AddTransient<AppdataFileDeleteJob>();
    collection.AddTransient<UpdateJob>();
    collection.AddTransient<HistoryCleanupJob>();
    collection.AddTransient<DeletedHistoryDataCleanupJob>();
    collection.AddTransient<OrphanedHistoryCleanupJob>();
    collection.AddTransient<LocalFileCacheCleanupJob>();
    await using var services = collection.BuildServiceProvider();
    List<string> checks = [];
    var canceledJobs = 0;

    async Task Execute<T>(CancellationToken token = default) where T : class, IJob
    {
        await using var scope = services.CreateAsyncScope();
        var context = new Mock<IJobExecutionContext>(MockBehavior.Strict);
        context.SetupGet(x => x.CancellationToken).Returns(token);
        await scope.ServiceProvider.GetRequiredService<T>().Execute(context.Object, token);
    }

    async Task Canceled<T>() where T : class, IJob
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await Execute<T>(cancellation.Token);
            throw new InvalidOperationException(typeof(T).Name + " swallowed cancellation.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            canceledJobs++;
        }
    }

    var oldDate = DateTime.Today.AddDays(-30);
    var oldFolder = Path.Combine(Env.AppDataFileFolder, oldDate.ToString("yyyyMMdd"));
    var newFolder = Path.Combine(Env.AppDataFileFolder, DateTime.Today.ToString("yyyyMMdd"));
    Directory.CreateDirectory(oldFolder);
    Directory.CreateDirectory(newFolder);
    Directory.CreateDirectory(Env.LogFolder);
    var oldLog = Path.Combine(Env.LogFolder, oldDate.ToString("yyyyMMdd") + ".txt");
    var newLog = Path.Combine(Env.LogFolder, DateTime.Today.ToString("yyyyMMdd") + ".txt");
    var oldDump = Path.Combine(Env.LogFolder, oldDate.ToString("yyyy-MM-dd HH-mm-ss") + ".dmp");
    foreach (var file in new[] { oldLog, newLog, oldDump }) File.WriteAllText(file, "fixture");
    foreach (var version in new[] { "1.0.0", "2.0.0", "3.0.0", "incomplete" }) Directory.CreateDirectory(Path.Combine(Env.UpdateFolder, version));
    var partial = Path.Combine(Env.UpdateFolder, "partial.tmp");
    File.WriteAllText(partial, "fixture");
    await Canceled<AppdataFileDeleteJob>();
    Check(Directory.Exists(oldFolder) && File.Exists(oldLog) && File.Exists(partial), "canceled file cleanup preserves data");
    await Execute<AppdataFileDeleteJob>();
    Check(!Directory.Exists(oldFolder) && Directory.Exists(newFolder), "temporary file retention");
    Check(!File.Exists(oldLog) && !File.Exists(oldDump) && File.Exists(newLog), "log and dump retention");
    Check(!Directory.Exists(Path.Combine(Env.UpdateFolder, "1.0.0")) && !Directory.Exists(Path.Combine(Env.UpdateFolder, "incomplete"))
        && !File.Exists(partial) && Directory.Exists(Path.Combine(Env.UpdateFolder, "2.0.0")) && Directory.Exists(Path.Combine(Env.UpdateFolder, "3.0.0")), "two newest update directories retained");
    checks.Add("AppdataFileDeleteJob: temporary files, logs, dumps and update packages");

    string[] recordNames = ["expired", "starred", "pinned", "synced", "recent", "deleted"];
    var rows = recordNames.Select((name, index) => new HistoryRecord
    {
        Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))),
        Text = name,
        Type = ProfileType.Text,
        Timestamp = index >= 4 ? DateTime.UtcNow : DateTime.UtcNow.AddDays(-30),
        Stared = index == 1,
        Pinned = index == 2,
        SyncStatus = index == 3 ? HistorySyncStatus.Synced : HistorySyncStatus.LocalOnly,
        IsDeleted = index == 5,
        IsLocalFileReady = index == 5,
        TransferDataFile = index == 5 ? "payload.txt" : null
    }).ToArray();
    history.RecordDbSet.AddRange(rows);
    await history.SaveChangesAsync(default);
    foreach (var record in rows)
    {
        var folder = history.GetRecordWorkingDir(record)!;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "payload.txt"), "fixture");
    }
    await Canceled<HistoryCleanupJob>();
    Check(await history.RecordDbSet.CountAsync() == 6, "canceled history cleanup preserves rows");
    // Hold the actual managed gate to cancel deterministically while cleanup is waiting.
    var historyGate = (SemaphoreSlim)typeof(HistoryManager).GetField("_dbSemaphore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(history)!;
    await historyGate.WaitAsync();
    using (var cancellation = new CancellationTokenSource())
    {
        try
        {
            var waitingCleanup = Execute<HistoryCleanupJob>(cancellation.Token);
            Check(!waitingCleanup.IsCompleted, "history cleanup waits for the database gate");
            cancellation.Cancel();
            try
            {
                await waitingCleanup.WaitAsync(TimeSpan.FromSeconds(10));
                throw new InvalidOperationException("History cleanup swallowed cancellation while waiting.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        finally
        {
            historyGate.Release();
        }
    }
    Check(await history.RecordDbSet.CountAsync() == 6, "cancellation while waiting preserves history");
    await Execute<HistoryCleanupJob>();
    using (var db = new HistoryDbContext())
    {
        Check(!await db.HistoryRecords.AnyAsync(x => x.Hash == rows[0].Hash), "expired row removed");
        Check(await db.HistoryRecords.CountAsync() == 5, "protected and recent rows retained");
    }
    Check(!Directory.Exists(history.GetRecordWorkingDir(rows[0])!), "expired working directory removed");
    checks.Add("HistoryCleanupJob: expired records and files, protected rows retained");

    await Canceled<DeletedHistoryDataCleanupJob>();
    Check(Directory.Exists(history.GetRecordWorkingDir(rows[5])!), "canceled deleted-data cleanup preserves files");
    await Execute<DeletedHistoryDataCleanupJob>();
    using (var db = new HistoryDbContext()) Check(!await db.HistoryRecords.AnyAsync(x => x.Hash == rows[5].Hash), "deleted row removed");
    Check(!Directory.Exists(history.GetRecordWorkingDir(rows[5])!), "deleted working directory removed");
    checks.Add("DeletedHistoryDataCleanupJob: deleted row and transfer files");

    var orphan = Directory.CreateDirectory(Path.Combine(Env.HistoryFileFolder, "orphan-aged"));
    var young = Directory.CreateDirectory(Path.Combine(Env.HistoryFileFolder, "orphan-young"));
    orphan.CreationTimeUtc = DateTime.UtcNow.AddDays(-30);
    orphan.Refresh();
    Check(orphan.CreationTimeUtc < DateTime.UtcNow.AddDays(-7), "fixture aged creation time");
    await Canceled<OrphanedHistoryCleanupJob>();
    Check(Directory.Exists(orphan.FullName), "canceled orphan cleanup preserves folders");
    await Execute<OrphanedHistoryCleanupJob>();
    Check(!Directory.Exists(orphan.FullName) && Directory.Exists(young.FullName)
        && Directory.Exists(history.GetRecordWorkingDir(rows[1])!), "only aged orphan removed");
    checks.Add("OrphanedHistoryCleanupJob: aged orphan removed, recent and owned directories retained");

    var cacheFile = Path.Combine(expectedData, "cache-fixture.txt");
    File.WriteAllText(cacheFile, "fixture");
    using (var db = new LocalFileCacheDbContext())
    {
        db.CacheEntries.AddRange(new LocalFileCacheEntry { Id = "present", CacheType = "probe", FilePath = cacheFile },
            new LocalFileCacheEntry { Id = "missing", CacheType = "probe", FilePath = cacheFile + ".missing" });
        await db.SaveChangesAsync();
    }
    await Canceled<LocalFileCacheCleanupJob>();
    using (var db = new LocalFileCacheDbContext()) Check(await db.CacheEntries.CountAsync() == 2, "canceled cache cleanup preserves rows");
    await Execute<LocalFileCacheCleanupJob>();
    using (var db = new LocalFileCacheDbContext()) Check(await db.CacheEntries.CountAsync() == 1 && await db.CacheEntries.AnyAsync(x => x.Id == "present"), "only missing-file cache entry removed");
    Check(File.Exists(cacheFile), "cache cleanup does not delete present files");
    checks.Add("LocalFileCacheCleanupJob: missing-file entry removed and present file retained");

    await Canceled<UpdateJob>();
    await Execute<UpdateJob>();
    Check(dispatches == 0, "disabled or canceled update does not dispatch");
    checks.Add("UpdateJob: disabled and canceled configuration");
    config.SetConfig(config.GetConfig<ProgramConfig>() with { CheckUpdateOnStartUp = true });
    await Execute<UpdateJob>();
    Check(dispatches == 1, "enabled update hands its callback to the substitute");
    checks.Add("UpdateJob: enabled callback identity without executing UI");
    failDispatch = true;
    try
    {
        await Execute<UpdateJob>();
        throw new InvalidOperationException("Dispatcher failure was swallowed.");
    }
    catch (InvalidOperationException error) when (error.Message == "Expected dispatcher failure.") { }
    failDispatch = false;
    await Execute<UpdateJob>();
    Check(dispatches == 3, "update recovers after dispatcher failure");
    checks.Add("UpdateJob: dispatcher failure propagation and subsequent recovery");

    await Execute<AppdataFileDeleteJob>();
    await Execute<HistoryCleanupJob>();
    await Execute<DeletedHistoryDataCleanupJob>();
    await Execute<OrphanedHistoryCleanupJob>();
    await Execute<LocalFileCacheCleanupJob>();
    using (var db = new HistoryDbContext()) Check(await db.HistoryRecords.CountAsync() == 4, "repeat cleanup preserves remaining history");
    checks.Add("Repeated cleanup is idempotent");
    Check(canceledJobs == 6, "all six cancellation entries observed");
    checks.Add("All six production job entry points honor pre-cancellation");
    notification.VerifyNoOtherCalls();
    window.VerifyNoOtherCalls();
    http.VerifyNoOtherCalls();
    Check(logger.Errors.Count == 0, "no swallowed cleanup errors: " + string.Join("; ", logger.Errors));
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        passed = true,
        quartzVersion = typeof(IJob).Assembly.GetName().Version?.ToString(),
        quartzSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(IJob).Assembly.Location))).ToLowerInvariant(),
        checks,
        canceledJobs,
        historyCancellationWhileWaiting = true,
        uiCallbacksExecuted = 0
    }, new JsonSerializerOptions { WriteIndented = true }));
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class ProbeProfileEnv : IProfileEnv
{
    public string GetPersistentDir() => Env.HistoryFileFolder;
    public string GetHistoryPersistentDir() => Env.HistoryFileFolder;
}

sealed class ProbeLogger : CoreLogger
{
    public List<string> Errors { get; } = [];
    public void Write(string? tag, string text) => Write(text);
    public void Write(string text)
    {
        if (text.Contains("failed", StringComparison.OrdinalIgnoreCase) || text.Contains("error", StringComparison.OrdinalIgnoreCase)) Errors.Add(text);
    }
    public Task WriteAsync(string? tag, string text) { Write(text); return Task.CompletedTask; }
    public Task WriteAsync(string text) { Write(text); return Task.CompletedTask; }
    public void Flush() { }
}
