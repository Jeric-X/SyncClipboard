using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History.HistoryExport;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Desktop.Views;
using SyncClipboard.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Utilities;

public sealed class HistoryExportDialogPresenter(IServiceProvider services, HistoryExporter exporter) : IHistoryExportDialog
{
    public async Task ShowAsync(IReadOnlyList<HistoryRecordKey>? selected = null, bool fromHistoryWindow = false)
    {
        var owner = fromHistoryWindow
            ? (Window)services.GetRequiredKeyedService<IWindow>("HistoryWindow")
            : (Window)services.GetRequiredService<IMainWindow>();
        var messages = new AvaloniaDialog(owner);
        using var session = exporter.TryBeginSession();
        if (session is null)
        {
            await messages.ShowMessageAsync(Strings.HistoryExport, Strings.HistoryExportBusy);
            return;
        }
        using var hold = fromHistoryWindow ? services.GetRequiredService<HistoryViewModel>().HoldForModalOperation() : null;
        var topmost = owner.Topmost;
        try
        {
            owner.Topmost = false;
            async Task<string?> PickDirectory()
            {
                var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = Strings.HistoryExportChooseDirectory,
                    AllowMultiple = false
                });
                return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            }
            var directory = await PickDirectory();
            if (directory is null)
                return;
            using var viewModel = services.GetRequiredService<HistoryExportViewModel>();
            var dialog = new HistoryExportDialog(viewModel,
                () => viewModel.InitializeAsync(directory, selected, PickDirectory));
            await dialog.ShowAsync(owner);
        }
        catch (Exception ex)
        {
            await messages.ShowMessageAsync(Strings.HistoryExport, string.Format(Strings.HistoryExportFailed, ex.Message));
        }
        finally
        {
            owner.Topmost = topmost;
        }
    }
}
