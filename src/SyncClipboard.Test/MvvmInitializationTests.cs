using Moq;
using NativeNotification.Interface;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.Utilities.Updater;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test;

[TestClass]
[TestCategory("NonUI")]
public class MvvmInitializationTests
{
    private string _directory = null!;
    private string _configPath = null!;
    private ConfigManager _config = null!;
    private int _changes;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"MvvmInitializationTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _configPath = Path.Combine(_directory, "SyncClipboard.json");
        _config = new ConfigManager(_configPath, new SyncClipboardConfigUpgrader());
        _config.ConfigChanged += () => _changes++;
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ServerSettings_LoadSilentlyAndPersistSubsequentChanges(bool saved)
    {
        var expected = new ServerConfig
        {
            SwitchOn = true,
            EnableHttps = true,
            CertificatePemPath = "certificate.pem",
            CertificatePemKeyPath = "key.pem",
            EnableCustomConfigurationFile = true,
            CustomConfigurationFilePath = "custom.json",
            MaxHistoryCount = 123,
            HistoryRetentionMinutes = 456
        };
        if (saved) _config.SetConfig(expected);
        else expected = _config.GetConfig<ServerConfig>();
        var original = BeginObservation();

        var model = new ServerConfigViewModel(_config);

        AssertUnchanged(original);
        Assert.AreEqual(expected, model.ServerConfig);
        Assert.AreEqual(expected.SwitchOn, model.ServerEnable);
        Assert.AreEqual(expected.EnableHttps, model.EnableHttps);
        Assert.AreEqual(expected.CertificatePemPath, model.CertificatePemPath);
        Assert.AreEqual(expected.CertificatePemKeyPath, model.CertificatePemKeyPath);
        Assert.AreEqual(expected.EnableCustomConfigurationFile, model.EnableCustomConfigurationFile);
        Assert.AreEqual(expected.CustomConfigurationFilePath, model.CustomConfigurationFilePath);
        Assert.AreEqual(expected.MaxHistoryCount, model.MaxHistoryCount);
        Assert.AreEqual(expected.HistoryRetentionMinutes, model.HistoryRetentionMinutes);

        model.CertificatePemKeyPath = "replacement.pem";
        Assert.AreEqual(1, _changes);
        Assert.AreEqual(expected with { CertificatePemKeyPath = "replacement.pem" }, _config.GetConfig<ServerConfig>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ClipboardSettings_LoadSilentlyAndPersistSubsequentChanges(bool saved)
    {
        var expected = new ClipboardAssistConfig { EasyCopyImageSwitchOn = true, DownloadWebImage = true, ConvertSwitchOn = true };
        if (saved) _config.SetConfig(expected);
        else expected = _config.GetConfig<ClipboardAssistConfig>();
        var original = BeginObservation();
        // Navigation is outside this contract; its dependency is retained but never invoked.
        var model = new CliboardAssistantViewModel(_config, null!);

        AssertUnchanged(original);
        Assert.AreEqual(expected, model.ClipboardAssistConfig);
        Assert.AreEqual(expected.EasyCopyImageSwitchOn, model.EasyCopyImageSwitchOn);
        Assert.AreEqual(expected.DownloadWebImage, model.DownloadWebImage);
        Assert.AreEqual(expected.ConvertSwitchOn, model.ConvertSwitchOn);

        model.DownloadWebImage = !expected.DownloadWebImage;
        Assert.AreEqual(1, _changes);
        Assert.AreEqual(expected with { DownloadWebImage = !expected.DownloadWebImage }, _config.GetConfig<ClipboardAssistConfig>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HistorySettings_DoNotPersistPartiallyLoadedValues(bool saved)
    {
        var expected = new HistoryConfig
        {
            EnableHistory = true,
            EnableSyncHistory = true,
            AutoDeleteMissingLocalFiles = true,
            MaxItemCount = 123,
            HistoryRetentionMinutes = 456
        };
        if (saved) _config.SetConfig(expected);
        else expected = _config.GetConfig<HistoryConfig>();
        var logger = new Mock<ILogger>(MockBehavior.Strict);
        var services = new Mock<IServiceProvider>(MockBehavior.Strict);
        services.Setup(x => x.GetService(typeof(ConfigManager))).Returns(_config);
        services.Setup(x => x.GetService(typeof(ILogger))).Returns(logger.Object);
        services.Setup(x => x.GetService(typeof(AccountManager))).Returns(new AccountManager(_config, logger.Object));
        // An empty account uses the inert EmptyRemoteClipboardServer and resolves no adapter.
        var factory = new RemoteClipboardServerFactory(services.Object);
        var original = BeginObservation();
        var dialog = new Mock<IMainWindowDialog>(MockBehavior.Strict);
        var model = new HistorySettingViewModel(_config, null!, dialog.Object, factory);

        AssertUnchanged(original);
        Assert.AreEqual(expected.EnableHistory, model.EnableHistory);
        Assert.AreEqual(expected.EnableSyncHistory, model.EnableSyncHistory);
        Assert.AreEqual(expected.AutoDeleteMissingLocalFiles, model.AutoDeleteMissingLocalFiles);
        Assert.AreEqual(expected.MaxItemCount, model.MaxItemCount);
        Assert.AreEqual(expected.HistoryRetentionMinutes, model.HistoryRetentionMinutes);
        Assert.IsFalse(model.ServerSyncSupported);

        model.MaxItemCount = expected.MaxItemCount + 1;
        Assert.AreEqual(1, _changes);
        Assert.AreEqual(expected with { MaxItemCount = expected.MaxItemCount + 1 }, _config.GetConfig<HistoryConfig>());
        dialog.VerifyNoOtherCalls();
        logger.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AboutSettings_LoadSilentlyAndPersistSubsequentChanges(bool saved)
    {
        var expected = new ProgramConfig { CheckUpdateOnStartUp = true, CheckUpdateForBeta = true, AutoDownloadUpdate = true };
        if (saved) _config.SetConfig(expected);
        else expected = _config.GetConfig<ProgramConfig>();
        var logger = new Mock<ILogger>(MockBehavior.Strict);
        logger.Setup(x => x.WriteAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var http = new Mock<IHttp>(MockBehavior.Strict);
        var window = new Mock<IMainWindow>(MockBehavior.Strict);
        var notification = new Mock<INotificationManager>(MockBehavior.Strict);
        // Idle status creation does not fetch releases or touch the clipboard/window/notification mocks.
        var updater = new UpdateChecker(null!, http.Object, logger.Object, null!, notification.Object,
            window.Object, _config, new ConfigBase());
        var original = BeginObservation();
        var model = new AboutViewModel(_config, Mock.Of<IAppConfig>(), updater);

        AssertUnchanged(original);
        Assert.AreEqual(expected.CheckUpdateOnStartUp, model.CheckUpdateOnStartUp);
        Assert.AreEqual(expected.CheckUpdateForBeta, model.CheckUpdateForBeta);
        Assert.AreEqual(expected.AutoDownloadUpdate, model.AutoDownloadUpdate);

        model.CheckUpdateForBeta = !expected.CheckUpdateForBeta;
        Assert.AreEqual(1, _changes);
        Assert.AreEqual(expected with { CheckUpdateForBeta = !expected.CheckUpdateForBeta }, _config.GetConfig<ProgramConfig>());
        http.VerifyNoOtherCalls();
        window.VerifyNoOtherCalls();
        notification.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SystemSettings_LoadWithoutPlatformCallbacksOrConfigNormalization(bool saved)
    {
        var expected = new ProgramConfig
        {
            Font = "saved font",
            Language = "unknown-locale",
            Theme = "unknown-theme",
            StartUpAsAdministrator = true,
            HideWindowOnStartup = true,
            DiagnoseMode = true,
            LogRemainDays = 23,
            TempFileRemainDays = 45
        };
        if (saved) _config.SetConfig(expected);
        else expected = _config.GetConfig<ProgramConfig>();
        var staticPath = Path.Combine(_directory, "StaticConfig.json");
        File.WriteAllText(staticPath, "{}");
        var staticConfig = new StaticConfig(staticPath);
        var staticChanges = 0;
        staticConfig.ConfigChanged += () => staticChanges++;
        var logger = new Mock<ILogger>(MockBehavior.Strict);
        var window = new Mock<IMainWindow>(MockBehavior.Strict);
        var services = new Mock<IServiceProvider>(MockBehavior.Strict);
        services.Setup(x => x.GetService(typeof(ILogger))).Returns(logger.Object);
        var original = BeginObservation();
        // Null startup metadata bypasses the real Windows Task Scheduler query on every OS.
        var model = new SystemSettingViewModel(_config, staticConfig, services.Object, taskRunAsAdministrator: null);

        AssertUnchanged(original);
        Assert.AreEqual("{}", File.ReadAllText(staticPath));
        Assert.AreEqual(0, staticChanges);
        Assert.AreEqual(expected, model.ProgramConfig);
        Assert.AreEqual(expected.Font, model.Font);
        Assert.AreEqual(expected.StartUpAsAdministrator, model.StartUpAsAdministrator);
        Assert.AreEqual(expected.HideWindowOnStartup, model.HideWindowOnStartUp);
        Assert.AreEqual(expected.DiagnoseMode, model.DiagnoseMode);
        Assert.AreEqual(expected.LogRemainDays, model.LogRemainDays);
        Assert.AreEqual(expected.TempFileRemainDays, model.TempFileRemainDays);
        Assert.AreEqual(SystemSettingViewModel.Languages[0], model.Language);
        Assert.AreEqual(SystemSettingViewModel.Themes[0], model.Theme);
        services.Verify(x => x.GetService(typeof(ILogger)), Times.Once);
        services.VerifyNoOtherCalls();
        logger.VerifyNoOtherCalls();

        services.Setup(x => x.GetService(typeof(IMainWindow))).Returns(window.Object);
        window.Setup(x => x.SetFont("replacement font"));
        model.Font = "replacement font";
        Assert.AreEqual("replacement font", _config.GetConfig<ProgramConfig>().Font);
        window.Verify(x => x.SetFont("replacement font"), Times.Once);
        window.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SyncSettings_LoadWithoutRoundingTheSavedByteLimit(bool saved)
    {
        var expected = new SyncConfig
        {
            SyncSwitchOn = true,
            PullSwitchOn = false,
            PushSwitchOn = false,
            MaxFileByte = 1234567,
            IntervalTime = 17,
            RetryTimes = 19,
            TimeOut = 23,
            DoNotUploadWhenCut = true,
            NotifyOnManualUpload = true,
            IgnoreExcludeForSyncSuggestion = true
        };
        if (saved) _config.SetConfig(expected);
        else expected = _config.GetConfig<SyncConfig>();
        // Leave existing clipboard-source loading inert; no source backend is instantiated.
        _config.SetConfig(new ClipboardFactoryConfig { ProhibitSources = ["wl-clipboard", "xclip", "Avalonia"] });
        var logger = new Mock<ILogger>(MockBehavior.Strict);
        var dialog = new Mock<IMainWindowDialog>(MockBehavior.Strict);
        var dispatcher = new Mock<IThreadDispatcher>(MockBehavior.Strict);
        var network = new Mock<INetworkContextProvider>(MockBehavior.Strict);
        var notification = new Mock<INotificationManager>(MockBehavior.Strict);
        var accounts = new AccountManager(_config, logger.Object);
        var switching = new NetworkAccountSwitchService(_config, accounts, network.Object, notification.Object, logger.Object);
        var original = BeginObservation();
        var model = new SyncSettingViewModel(_config, null!, accounts, dialog.Object, dispatcher.Object, switching);

        AssertUnchanged(original);
        Assert.AreEqual(expected, model.ClientConfig);
        Assert.AreEqual(expected.MaxFileByte / 1024 / 1024, model.MaxFileSize);
        Assert.AreEqual(expected.IntervalTime, model.IntervalTime);
        Assert.AreEqual(expected.RetryTimes, model.RetryTimes);
        Assert.AreEqual(expected.TimeOut, model.TimeOut);
        Assert.AreEqual(expected.SyncSwitchOn, model.SyncEnable);
        Assert.AreEqual(expected.PullSwitchOn, model.DownloadEnable);
        Assert.AreEqual(expected.PushSwitchOn, model.UploadEnable);

        model.RetryTimes = expected.RetryTimes + 1;
        Assert.AreEqual(1, _changes);
        Assert.AreEqual(expected with { RetryTimes = expected.RetryTimes + 1 }, _config.GetConfig<SyncConfig>());
        dialog.VerifyNoOtherCalls();
        dispatcher.VerifyNoOtherCalls();
        network.VerifyNoOtherCalls();
        notification.VerifyNoOtherCalls();
        logger.VerifyNoOtherCalls();
    }

    private string BeginObservation()
    {
        _changes = 0;
        return File.ReadAllText(_configPath);
    }

    private void AssertUnchanged(string original)
    {
        Assert.AreEqual(0, _changes, "Construction must not publish config changes.");
        Assert.AreEqual(original, File.ReadAllText(_configPath), "Construction must not rewrite the saved config.");
    }
}
