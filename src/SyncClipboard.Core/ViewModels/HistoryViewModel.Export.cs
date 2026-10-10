using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel
{
    private int _modalOperations;
    public bool HasModalOperation => _modalOperations > 0;

    [RelayCommand(CanExecute = nameof(HasSelectedRecords))]
    private Task ExportSelectedAsync()
    {
        if (!HasSelectedRecords)
            return Task.CompletedTask;
        return _serviceProvider.GetRequiredService<IHistoryExportDialog>()
            .ShowAsync(selectedHistoryRecords.Keys.ToArray(), fromHistoryWindow: true);
    }

    public IDisposable HoldForModalOperation()
    {
        _modalOperations++;
        return new ScopeGuard(() => _modalOperations--);
    }
}
