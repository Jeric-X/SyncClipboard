using Microsoft.Extensions.Options;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Server.Core.Services.History;

class HistoryCleaner(IServiceProvider serviceProvider, IOptions<AppSettings> options, ILogger<HistoryCleaner> logger) : IHostedService
{
    private readonly CancellationTokenSource _cts = new();
    public Task StartAsync(CancellationToken _)
    {
        Task.Factory.StartNew(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using var scope = serviceProvider.CreateScope();
                    var historyService = scope.ServiceProvider.GetRequiredService<HistoryService>();
                    await historyService.SetRecordsMaxCount(options.Value.MaxSavedHistoryCount, _cts.Token);
                }
                catch (Exception ex) when (!_cts.IsCancellationRequested)
                {
                    logger.LogError(ex, "Error occurred when cleaning history records.");
                }

                await Task.Delay(TimeSpan.FromMinutes(10), _cts.Token);
            }
        }, TaskCreationOptions.LongRunning);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        return Task.CompletedTask;
    }
}