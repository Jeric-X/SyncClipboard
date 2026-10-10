using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.History.HistoryImport;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Desktop.Views;
using SyncClipboard.Desktop.Services;
using System;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Utilities;

public sealed class HistoryImportDialogPresenter(IServiceProvider services, HistoryImporter importer) : IHistoryImportDialog
{
    public async Task ShowAsync()
    {
        var owner = (Window)services.GetRequiredService<IMainWindow>();
        var messages = new AvaloniaDialog(owner);
        using var session = importer.TryBeginSession();
        if (session is null)
        {
            await messages.ShowMessageAsync(Strings.HistoryImport, Strings.HistoryImportBusy);
            return;
        }
        try
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Strings.HistoryImportChooseFile,
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(Strings.HistoryExportArchive) { Patterns = ["*.json", "*.zip"] }]
            });
            var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (path is null)
                return;
            using var viewModel = services.GetRequiredService<HistoryImportViewModel>();
            var dialog = new HistoryImportDialog(viewModel, () => viewModel.InitializeAsync(path));
            await dialog.ShowAsync(owner);
        }
        catch (Exception ex)
        {
            await messages.ShowMessageAsync(Strings.HistoryImport, string.Format(Strings.HistoryImportFailed, ex.Message));
        }
    }
}
