using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.WinUI3;

namespace SyncClipboard.Test.WinUI3;

[TestClass]
public class ServiceProvider
{
    Microsoft.Extensions.DependencyInjection.ServiceProvider? Services { get; set; }

    [TestInitialize]
    public void InitializeServices()
    {
        var servicesCollection = AppServices.ConfigureServices();
        servicesCollection.AddSingleton<IMainWindow>(new Mock<IMainWindow>().Object);
        servicesCollection.AddSingleton<IContextMenu>(new Mock<IContextMenu>().Object);
        Services = servicesCollection.BuildServiceProvider();
    }

    [TestMethod]
    [TestCategory("RequiresUI")]
    [SystemServiceProviderDataSource]
    [PlatformServiceProviderDataSource]
    public void ConfigedServices(Type type)
    {
        Assert.IsNotNull(Services?.GetService(type));
    }

    [TestMethod]
    [TestCategory("NonUI")]
    [DataRow(typeof(IAppConfig))]
    [DataRow(typeof(IGlobalDialog))]
    [DataRow(typeof(IClipboardSetter<TextProfile>))]
    [DataRow(typeof(IClipboardSetter<FileProfile>))]
    public void ConfiguredNonUiServices_CanBeResolved(Type type)
    {
        Assert.IsNotNull(Services?.GetRequiredService(type));
    }

    [TestCleanup]
    public void CleanupServices()
    {
        Services?.Dispose();
    }
}
