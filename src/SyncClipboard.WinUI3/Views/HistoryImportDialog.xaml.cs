using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HistoryImportDialog : ContentDialog
{
    public HistoryImportDialog() => InitializeComponent();

    public HistoryImportDialog(HistoryImportViewModel viewModel, Func<Task> initialize) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) =>
        {
            if (initialize != null)
                await initialize();
        };
        Closing += (_, args) =>
        {
            if (!viewModel.IsBusy)
                return;
            args.Cancel = true;
            viewModel.Cancel();
        };
        void UpdateCloseButton(object? sender, PropertyChangedEventArgs args) =>
            CloseButtonText = viewModel.IsBusy ? Strings.Cancel : Strings.HistoryExportClose;
        viewModel.PropertyChanged += UpdateCloseButton;
        Closed += (_, _) => viewModel.PropertyChanged -= UpdateCloseButton;
    }
}
