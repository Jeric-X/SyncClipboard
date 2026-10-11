using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Views;

public partial class HistoryImportDialog : FAContentDialog
{
    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    public HistoryImportDialog() => InitializeComponent();

    public HistoryImportDialog(HistoryImportViewModel viewModel, Func<Task> initialize) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) => await initialize();
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
