using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Options;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class LoggerTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"LoggerTests-{Guid.NewGuid():N}");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public void Logger_WritesWithoutConfigManagerAndObservesOptionChanges()
    {
        var option = new LoggerOption { Path = _directory };
        using var logger = new Logger(option);

        logger.Write("Test", "startup failure");
        var logPath = Directory.EnumerateFiles(_directory, "*.txt").Single();
        Assert.Contains("startup failure", ReadLog(logPath));

        option.FlushImmediately = false;
        logger.Write("Test", "buffered message");
        option.FlushImmediately = true;
        logger.Write("Test", "flush trigger");

        var log = ReadLog(logPath);
        Assert.Contains("buffered message", log);
        Assert.Contains("flush trigger", log);
    }

    [TestMethod]
    public async Task ConfigRecoveryService_LogsStartupFailureBeforePrompting()
    {
        var option = new LoggerOption { Path = _directory };
        using var logger = new Logger(option);
        var service = new ConfigRecoveryService(new DecliningGlobalDialog(), logger);

        var result = await service.ExecuteWithRecoveryAsync<object>(
            () => throw new SyncClipboardConfigUpgradeException("configuration load failed"),
            () => Path.Combine(_directory, "SyncClipboard.json"),
            "Application startup failed",
            "Application startup");

        Assert.IsNull(result);
        var logPath = Directory.EnumerateFiles(_directory, "*.txt").Single();
        var log = ReadLog(logPath);
        Assert.Contains("Application startup failed", log);
        Assert.Contains("configuration load failed", log);
    }

    private static string ReadLog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class DecliningGlobalDialog : IGlobalDialog
    {
        public Task<bool> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText) => Task.FromResult(false);

        public Task ShowMessageAsync(string title, string message, string closeButtonText) => Task.CompletedTask;
    }
}
