namespace SyncClipboard.Test.Winform;

[TestClass]
public class ServiceProvider
{
    IServiceProvider Services { get; set; }
    public ServiceProvider()
    {
        Services = Global.ConfigurateServices();
    }

    [TestMethod]
    [SystemServiceProviderDataSource]
    [PlatformServiceProviderDataSource]
    public void ConfigedServices(Type type)
    {
        Assert.IsNotNull(Services.GetService(type));
    }
}