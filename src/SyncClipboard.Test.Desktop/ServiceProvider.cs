using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Desktop;

namespace SyncClipboard.Test.Desktop;

[TestClass]
public class ServiceProvider
{
    Microsoft.Extensions.DependencyInjection.ServiceProvider? Services { get; set; }

    [TestInitialize]
    public void InitializeServices()
    {
        var servicesCollection = AppServices.ConfigureServices();
        //servicesCollection.AddSingleton<IMainWindow>(new Mock<IMainWindow>().Object);  
        servicesCollection.AddSingleton<IContextMenu>(new Mock<IContextMenu>().Object);
        Services = servicesCollection.BuildServiceProvider();
    }

    [TestMethod]
    [SystemServiceProviderDataSource]
    [PlatformServiceProviderDataSource]
    public void ConfigedServices(Type type)
    {
        Assert.IsNotNull(Services?.GetService(type));
    }
}