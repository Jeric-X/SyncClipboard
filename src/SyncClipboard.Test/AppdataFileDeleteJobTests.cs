using Moq;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Job;

namespace SyncClipboard.Test;

[TestClass]
public class AppdataFileDeleteJobTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationTokenSource.Token;
    private string _directory = null!;
    private readonly List<string> _messages = [];
    private Mock<ILogger> _logger = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"AppdataFileDeleteJobTests-{Guid.NewGuid():N}");
        _messages.Clear();
        _logger = new Mock<ILogger>();
        _logger.Setup(logger => logger.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, message) => _messages.Add(message));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            }
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public void ReadOnlyCache_DoesNotBlockOtherExpiredCacheOrLogs()
    {
        RequireWindows();
        var readOnlyFile = CreateFile("file/20000101/read-only.txt");
        File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);
        var otherCache = CreateFile("file/20000102/normal.txt");
        var log = CreateFile("log/20000101.txt");
        var dump = CreateFile("log/2000-01-01 00-00-00.dmp");

        // This is the deletion used by the original job, and reproduces the reported Windows failure.
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => new DirectoryInfo(Path.GetDirectoryName(readOnlyFile)!).Delete(true));

        RunCleanup(Token);

        Assert.IsTrue(File.Exists(readOnlyFile));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(otherCache)));
        Assert.IsFalse(File.Exists(log));
        Assert.IsFalse(File.Exists(dump));
        Assert.IsTrue(_messages.Any(message => message.Contains("cache")
            && message.Contains(Path.GetDirectoryName(readOnlyFile)!)
            && message.Contains(nameof(UnauthorizedAccessException))));
    }

    [TestMethod]
    [TestCategory("PlatformWindows")]
    [DataRow("update/locked.zip", "updates")]
    [DataRow("update/incomplete/locked.txt", "updates")]
    [DataRow("update/1.0.0/locked.txt", "updates")]
    [DataRow("file/20000101/locked.txt", "cache")]
    [DataRow("log/20000101.txt", "logs")]
    [DataRow("log/2000-01-01 00-00-00.dmp", "dumps")]
    public void LockedEntry_DoesNotBlockOtherExpiredEntries(string relativePath, string stage)
    {
        RequireWindows();
        var lockedFile = CreateFile(relativePath);
        var expiredFiles = new[]
        {
            CreateFile("update/package.zip"),
            CreateFile("update/obsolete/data.txt"),
            CreateFile("update/1.1.0/data.txt"),
            CreateFile("file/20000102/data.txt"),
            CreateFile("log/20000102.txt"),
            CreateFile("log/2000-01-02 00-00-00.dmp")
        };
        var latestUpdate = CreateFile("update/3.0.0/data.txt");
        var previousUpdate = CreateFile("update/2.0.0/data.txt");
        using var stream = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);

        RunCleanup(Token);

        Assert.IsTrue(File.Exists(lockedFile));
        foreach (var file in expiredFiles)
        {
            Assert.IsFalse(File.Exists(file), file);
        }
        Assert.IsTrue(File.Exists(latestUpdate));
        Assert.IsTrue(File.Exists(previousUpdate));
        Assert.IsTrue(_messages.Any(message => message.Contains(stage) && message.Contains(nameof(IOException))));
    }

    [TestMethod]
    public void Retention_DeletesOnlyExpiredDatesAndKeepsUnrecognizedNames()
    {
        var today = DateTime.Today;
        var retainedFiles = new[]
        {
            CreateFile($"file/{today.AddDays(-1):yyyyMMdd}/boundary.txt"),
            CreateFile($"file/{today:yyyyMMdd}/recent.txt"),
            CreateFile("file/history/data.txt"),
            CreateFile("file/00000000/data.txt"),
            CreateFile($"log/{today.AddDays(-8):yyyyMMdd}.txt"),
            CreateFile($"log/{today:yyyyMMdd}.txt"),
            CreateFile("log/00000000.txt"),
            CreateFile("log/0000-00-00 00-00-00.dmp")
        };
        var expiredFiles = new[]
        {
            CreateFile($"file/{today.AddDays(-2):yyyyMMdd}/expired.txt"),
            CreateFile($"log/{today.AddDays(-9):yyyyMMdd}.txt"),
            CreateFile($"log/{today.AddDays(-9):yyyy-MM-dd HH-mm-ss}.dmp")
        };

        RunCleanup(Token);

        foreach (var file in retainedFiles)
        {
            Assert.IsTrue(File.Exists(file), file);
        }
        foreach (var file in expiredFiles)
        {
            Assert.IsFalse(File.Exists(file), file);
        }
        Assert.IsEmpty(_messages);
    }

    [TestMethod]
    public void DisabledRetention_KeepsCacheAndLogsButStillCleansUpdates()
    {
        var cache = CreateFile("file/20000101/data.txt");
        var log = CreateFile("log/20000101.txt");
        var dump = CreateFile("log/2000-01-01 00-00-00.dmp");
        var update = CreateFile("update/package.zip");

        RunCleanup(Token, new ProgramConfig { TempFileRemainDays = 0, LogRemainDays = 0 });

        Assert.IsTrue(File.Exists(cache));
        Assert.IsTrue(File.Exists(log));
        Assert.IsTrue(File.Exists(dump));
        Assert.IsFalse(File.Exists(update));
    }

    [TestMethod]
    public void MissingCacheAndUpdateFolders_DoNotBlockLogCleanup()
    {
        var log = CreateFile("log/20000101.txt");

        RunCleanup(Token);

        Assert.IsFalse(File.Exists(log));
        Assert.IsEmpty(_messages);
    }

    [TestMethod]
    public void CancellationBeforeCleanup_PropagatesAndKeepsFiles()
    {
        var update = CreateFile("update/package.zip");
        var log = CreateFile("log/20000101.txt");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => RunCleanup(token: cancellation.Token));

        Assert.IsTrue(File.Exists(update));
        Assert.IsTrue(File.Exists(log));
        Assert.IsEmpty(_messages);
    }

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public void CancellationDuringCleanup_PropagatesAndStopsLaterStages()
    {
        RequireWindows();
        var update = CreateFile("update/locked.zip");
        var log = CreateFile("log/20000101.txt");
        using var stream = new FileStream(update, FileMode.Open, FileAccess.Read, FileShare.None);
        using var cancellation = new CancellationTokenSource();
        _logger.Setup(logger => logger.Write(It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => cancellation.Cancel());

        Assert.ThrowsExactly<OperationCanceledException>(() => RunCleanup(token: cancellation.Token));

        Assert.IsTrue(File.Exists(log));
    }

    private void RunCleanup(CancellationToken token, ProgramConfig? config = null)
    {
        AppdataFileDeleteJob.PlannedTask(config ?? new ProgramConfig(), _directory, _logger.Object, token);
    }

    private string CreateFile(string relativePath)
    {
        var path = Path.Combine(_directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test data");
        return path;
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Requires Windows file deletion semantics.");
        }
    }
}
