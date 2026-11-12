using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel
{
    private int _modalOperations;
    private bool _remainWindowForFolderPicker;
    public bool HasModalOperation => _modalOperations > 0;

    [RelayCommand(CanExecute = nameof(HasSelectedRecords))]
    private Task ExportSelectedAsync()
    {
        if (!HasSelectedRecords)
            return Task.CompletedTask;
        return _serviceProvider.GetRequiredService<IHistoryExportDialog>()
            .ShowAsync(selectedHistoryRecords.Keys.ToArray(), fromHistoryWindow: true);
    }

    public IDisposable HoldForNativeFolderPicker()
    {
        _remainWindowForFolderPicker = true;
        return new ScopeGuard(() => _remainWindowForFolderPicker = false);
    }

    public IDisposable HoldForModalOperation()
    {
        _modalOperations++;
        return new ScopeGuard(() => _modalOperations--);
    }
}
