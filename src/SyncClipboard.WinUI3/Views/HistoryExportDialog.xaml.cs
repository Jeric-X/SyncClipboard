using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HistoryExportDialog : ContentDialog
{
    public HistoryExportDialog() => InitializeComponent();

    public HistoryExportDialog(HistoryExportViewModel viewModel, Func<Task> initialize) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) => await initialize?.Invoke();
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
