using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core;
using SyncClipboard.Core.Commons.ConfigMigration;

namespace SyncClipboard.Test;

public sealed class ConfigurationTestServices : IDisposable
{
    private readonly ServiceProvider _provider;

    public ConfigurationTestServices()
    {
        var services = new ServiceCollection();
        AppCore.ConfigCommonService(services);
        _provider = services.BuildServiceProvider();
    }

    public SyncClipboardConfigUpgrader Upgrader => _provider.GetRequiredService<SyncClipboardConfigUpgrader>();

    public void Dispose() => _provider.Dispose();
}
