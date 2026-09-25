using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Desktop;
using SyncClipboard.Desktop.ClipboardAva.ClipboardReader;
using SyncClipboard.Desktop.ViewModels;
using System.Reflection;

namespace SyncClipboard.Test.Desktop;

[TestClass]
public class DiagnoseViewModelTests
{
    private ConfigurationTestServices _migrationServices = null!;
    private DirectoryInfo _directory = null!;
    private ConfigManager _config = null!;
    private Mock<IClipboardChangingListener> _listener = null!;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider _services = null!;

    [TestInitialize]
    public void Initialize()
    {
        _migrationServices = new ConfigurationTestServices();
        _directory = Directory.CreateTempSubdirectory("SyncClipboard-diagnose-");
        _config = (ConfigManager)Activator.CreateInstance(typeof(ConfigManager),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [Path.Combine(_directory.FullName, "config.json"), _migrationServices.Upgrader], null)!;
        _listener = new Mock<IClipboardChangingListener>();

        var services = AppServices.ConfigureServices();
        services.AddSingleton(_config);
        services.AddSingleton(_listener.Object);
        services.AddSingleton(new ClipboardReaderSelector([], _config));
        _services = services.BuildServiceProvider();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _services.Dispose();
        _migrationServices.Dispose();
        _directory.Delete(true);
    }

    [TestMethod]
    public void RepeatedResolutionSharesViewModelAndClipboardSubscription()
    {
        _config.SetConfig(new ProgramConfig { DiagnoseMode = true, DiagnosePageAutoRefresh = true });
        var first = _services.GetRequiredService<DiagnoseViewModel>();
        using var scope = _services.CreateScope();
        var second = scope.ServiceProvider.GetRequiredService<DiagnoseViewModel>();

        Assert.AreSame(first, second);
        _listener.VerifyAdd(listener => listener.Changed += It.IsAny<ClipboardChangedDelegate>(), Times.Once);
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(false, false)]
    public void ConfigChangesDoNotDuplicateSubscriptionsAndDisablingUnsubscribes(bool diagnoseMode, bool autoRefresh)
    {
        var enabled = new ProgramConfig { DiagnoseMode = true, DiagnosePageAutoRefresh = true };
        _config.SetConfig(enabled);
        var viewModel = _services.GetRequiredService<DiagnoseViewModel>();

        _config.SetConfig(enabled with { Theme = "Dark" });
        _config.SetConfig(enabled with { Theme = "Light" });
        _listener.VerifyAdd(listener => listener.Changed += It.IsAny<ClipboardChangedDelegate>(), Times.Once);
        _listener.VerifyRemove(listener => listener.Changed -= It.IsAny<ClipboardChangedDelegate>(), Times.Never);

        var disabled = enabled with { DiagnoseMode = diagnoseMode, DiagnosePageAutoRefresh = autoRefresh };
        _config.SetConfig(disabled);
        _config.SetConfig(disabled with { Theme = "Dark" });
        Assert.AreEqual(autoRefresh, viewModel.AutoRefresh);
        _listener.VerifyAdd(listener => listener.Changed += It.IsAny<ClipboardChangedDelegate>(), Times.Once);
        _listener.VerifyRemove(listener => listener.Changed -= It.IsAny<ClipboardChangedDelegate>(), Times.Once);

        _config.SetConfig(enabled);
        _listener.VerifyAdd(listener => listener.Changed += It.IsAny<ClipboardChangedDelegate>(), Times.Exactly(2));
        _listener.VerifyRemove(listener => listener.Changed -= It.IsAny<ClipboardChangedDelegate>(), Times.Once);
    }

    [TestMethod]
    public void DisabledAutoRefreshDoesNotSubscribeUntilEnabled()
    {
        var viewModel = _services.GetRequiredService<DiagnoseViewModel>();
        _config.SetConfig(new ProgramConfig { Theme = "Dark" });
        _config.SetConfig(new ProgramConfig { DiagnoseMode = true });
        _listener.VerifyAdd(listener => listener.Changed += It.IsAny<ClipboardChangedDelegate>(), Times.Never);
        _listener.VerifyRemove(listener => listener.Changed -= It.IsAny<ClipboardChangedDelegate>(), Times.Never);

        viewModel.AutoRefresh = true;
        Assert.IsTrue(_config.GetConfig<ProgramConfig>().DiagnosePageAutoRefresh);
        _listener.VerifyAdd(listener => listener.Changed += It.IsAny<ClipboardChangedDelegate>(), Times.Once);
    }
}
