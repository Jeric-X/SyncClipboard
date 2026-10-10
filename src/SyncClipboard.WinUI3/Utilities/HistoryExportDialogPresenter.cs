using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History.HistoryExport;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.WinUI3.Views;
using SyncClipboard.WinUI3.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyncClipboard.WinUI3.Utilities;

public sealed class HistoryExportDialogPresenter(IServiceProvider services, HistoryExporter exporter) : IHistoryExportDialog
{
    public async Task ShowAsync(IReadOnlyList<HistoryRecordKey>? selected = null, bool fromHistoryWindow = false)
    {
        var owner = fromHistoryWindow
            ? (Window)services.GetRequiredKeyedService<IWindow>(nameof(HistoryWindow))
            : (Window)services.GetRequiredService<IMainWindow>();
        var messages = new WinUIDialog(owner);
        using var session = exporter.TryBeginSession();
        if (session is null)
        {
            await messages.ShowMessageAsync(Strings.HistoryExport, Strings.HistoryExportBusy);
            return;
        }
        var history = fromHistoryWindow ? services.GetRequiredService<HistoryViewModel>() : null;
        using var hold = history?.HoldForModalOperation();
        try
        {
            if (history is not null)
                ((IWindow)owner).SetTopmost(false);
            async Task<string?> PickDirectory()
            {
                var picker = new FolderPicker(owner.AppWindow.Id)
                {
                    SuggestedStartLocation = PickerLocationId.ComputerFolder,
                    CommitButtonText = Strings.HistoryExportChooseDirectory
                };
                return (await picker.PickSingleFolderAsync())?.Path;
            }
            var directory = await PickDirectory();
            if (directory is null)
                return;
            using var viewModel = services.GetRequiredService<HistoryExportViewModel>();
            var dialog = new HistoryExportDialog(viewModel,
                () => viewModel.InitializeAsync(directory, selected, PickDirectory))
            { XamlRoot = owner.Content.XamlRoot };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            await messages.ShowMessageAsync(Strings.HistoryExport, string.Format(Strings.HistoryExportFailed, ex.Message));
        }
        finally
        {
            if (history is not null)
                ((IWindow)owner).SetTopmost(history.IsTopmost);
        }
    }
}
