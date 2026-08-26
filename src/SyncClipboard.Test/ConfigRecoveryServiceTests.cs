using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using System.Text.Json.Nodes;

namespace SyncClipboard.Test;

[TestClass]
public class ConfigRecoveryServiceTests
{
    private string _directory = null!;
    private string _configPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"ConfigRecoveryServiceTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _configPath = Path.Combine(_directory, "SyncClipboard.json");
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
    public async Task TryRecoverAsync_WhenConfirmed_BacksUpInvalidFileAndWritesDefaults()
    {
        const string invalidJson = "{ invalid";
        File.WriteAllText(_configPath, invalidJson);
        var dialog = new FakeGlobalDialog(confirmationResult: true);
        var logger = new FakeLogger();
        var service = new ConfigRecoveryService(dialog, logger);

        var recovered = await service.TryRecoverAsync(
            _configPath,
            new SyncClipboardConfigUpgradeException("configuration load failed"));

        Assert.IsTrue(recovered);
        var root = JsonNode.Parse(File.ReadAllText(_configPath))!.AsObject();
        Assert.AreEqual(Env.SyncClipboardConfigVersion, root[SyncClipboardConfigUpgrader.VersionPropertyName]!.GetValue<int>());
        var backup = Directory.EnumerateFiles(Path.Combine(_directory, "config_backup"), "*.json").Single();
        Assert.AreEqual(invalidJson, File.ReadAllText(backup));
        Assert.AreEqual(1, dialog.ConfirmationCount);
        Assert.AreEqual(0, dialog.MessageCount);
        Assert.IsTrue(logger.Messages.Any(message => message.Contains("Replaced invalid configuration")));
    }

    [TestMethod]
    public async Task TryRecoverAsync_WhenDeclined_PreservesInvalidFile()
    {
        const string invalidJson = "{ invalid";
        File.WriteAllText(_configPath, invalidJson);
        var dialog = new FakeGlobalDialog(confirmationResult: false);
        var service = new ConfigRecoveryService(dialog, new FakeLogger());

        var recovered = await service.TryRecoverAsync(
            _configPath,
            new SyncClipboardConfigUpgradeException("configuration load failed"));

        Assert.IsFalse(recovered);
        Assert.AreEqual(invalidJson, File.ReadAllText(_configPath));
        Assert.IsFalse(Directory.Exists(Path.Combine(_directory, "config_backup")));
        Assert.AreEqual(1, dialog.ConfirmationCount);
    }

    [TestMethod]
    public void IsConfigurationError_RecognizesWrappedUpgradeException()
    {
        var exception = new InvalidOperationException(
            "outer",
            new SyncClipboardConfigUpgradeException("configuration load failed"));

        Assert.IsTrue(ConfigRecoveryService.IsConfigurationError(exception));
        Assert.IsFalse(ConfigRecoveryService.IsConfigurationError(new InvalidOperationException("other failure")));
    }

    [TestMethod]
    public async Task ExecuteWithRecoveryAsync_WhenOtherFailureIsRetried_RepeatsUntilSuccess()
    {
        var attempts = 0;
        var dialog = new FakeGlobalDialog(confirmationResult: true);
        var service = new ConfigRecoveryService(dialog, new FakeLogger());

        var result = await service.ExecuteWithRecoveryAsync(
            () => ++attempts == 1 ? throw new InvalidOperationException("temporary failure") : 42,
            () => _configPath,
            "Operation failed",
            "Test operation");

        Assert.AreEqual(42, result);
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(1, dialog.ConfirmationCount);
    }

    [TestMethod]
    public async Task TryRestoreCurrentConfigAsync_WhenConfirmed_RestoresActiveConfiguration()
    {
        var restored = false;
        var dialog = new FakeGlobalDialog(confirmationResult: true);
        var service = new ConfigRecoveryService(dialog, new FakeLogger());

        var result = await service.TryRestoreCurrentConfigAsync(
            _configPath,
            () => restored = true,
            new InvalidOperationException("reload failed"));

        Assert.IsTrue(result);
        Assert.IsTrue(restored);
        Assert.AreEqual(1, dialog.ConfirmationCount);
        Assert.AreEqual(Strings.RestoreCurrentConfig, dialog.PrimaryButtonTexts.Single());
    }

    private sealed class FakeGlobalDialog(bool confirmationResult) : IGlobalDialog
    {
        public int ConfirmationCount { get; private set; }
        public int MessageCount { get; private set; }
        public List<string> PrimaryButtonTexts { get; } = [];

        public Task<bool> ShowConfirmationAsync(
            string title,
            string message,
            string primaryButtonText,
            string closeButtonText)
        {
            ConfirmationCount++;
            PrimaryButtonTexts.Add(primaryButtonText);
            return Task.FromResult(confirmationResult);
        }

        public Task ShowMessageAsync(string title, string message, string closeButtonText)
        {
            MessageCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public void Write(string? tag, string str) => Messages.Add($"{tag}: {str}");

        public void Write(string str) => Messages.Add(str);

        public Task WriteAsync(string? tag, string str)
        {
            Write(tag, str);
            return Task.CompletedTask;
        }

        public Task WriteAsync(string str)
        {
            Write(str);
            return Task.CompletedTask;
        }

        public void Flush()
        {
        }
    }
}
