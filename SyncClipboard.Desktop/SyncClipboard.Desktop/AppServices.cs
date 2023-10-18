using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core;

namespace SyncClipboard.Desktop;

public class AppServices
{
    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        ProgramWorkflow.ConfigCommonService(services);
        ProgramWorkflow.ConfigurateViewModels(services);

        return services;
    }
}
