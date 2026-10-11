using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.History.HistoryImport;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.WinUI3.Views;
using SyncClipboard.WinUI3.Services;
using System;
using System.Threading.Tasks;

namespace SyncClipboard.WinUI3.Utilities;

public sealed class HistoryImportDialogPresenter(IServiceProvider services, HistoryImporter importer) : IHistoryImportDialog
{
    public async Task ShowAsync()
    {
        var owner = (Window)services.GetRequiredService<IMainWindow>();
        var messages = new WinUIDialog(owner);
        using var session = importer.TryBeginSession();
        if (session is null)
        {
            await messages.ShowMessageAsync(Strings.HistoryImport, Strings.HistoryImportBusy);
            return;
        }
        try
        {
            var picker = new FileOpenPicker(owner.AppWindow.Id);
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".zip");
            var path = (await picker.PickSingleFileAsync())?.Path;
            if (path is null)
                return;
            using var viewModel = services.GetRequiredService<HistoryImportViewModel>();
            var dialog = new HistoryImportDialog(viewModel, () => viewModel.InitializeAsync(path))
            { XamlRoot = owner.Content.XamlRoot };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            await messages.ShowMessageAsync(Strings.HistoryImport, string.Format(Strings.HistoryImportFailed, ex.Message));
        }
    }
}
