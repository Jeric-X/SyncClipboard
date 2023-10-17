using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Core;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;

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
