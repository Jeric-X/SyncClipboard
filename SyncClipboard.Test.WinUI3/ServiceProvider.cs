//using SyncClipboard.WinUI3;

namespace SyncClipboard.Test.WinUI3;

[TestClass]
public class ServiceProvider
{
    IServiceProvider? Services { get; set; }
    //public ServiceProvider()
    //{
    //    //Services = new App().Services;
    //}

    [TestMethod]
    [SystemServiceProviderDataSource]
    [PlatformServiceProviderDataSource]
    public void ConfigedServices(Type type)
    {
        Assert.IsNotNull(null);
    }
}