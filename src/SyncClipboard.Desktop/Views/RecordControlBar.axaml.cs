using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;

namespace SyncClipboard.Desktop.Views;

public partial class RecordControlBar : UserControl
{
    public HistoryViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty) as HistoryViewModel;
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly StyledProperty<HistoryViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<RecordControlBar, HistoryViewModel?>(nameof(ViewModel));

    public HistoryRecordVM? Record
    {
        get => GetValue(RecordProperty) as HistoryRecordVM;
        set => SetValue(RecordProperty, value);
    }

    public static readonly StyledProperty<HistoryRecordVM?> RecordProperty =
        AvaloniaProperty.Register<RecordControlBar, HistoryRecordVM?>(nameof(Record));

    public RecordControlBar()
    {
        InitializeComponent();
    }

    private void DownloadButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (Record != null && ViewModel != null)
        {
            ViewModel.DownloadRemoteProfileCommand.Execute(Record);
        }
    }

    private void CancelDownloadButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (Record != null && ViewModel != null)
        {
            ViewModel.CancelDownloadCommand.Execute(Record);
        }
    }

    private void UploadButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (Record != null && ViewModel != null)
        {
            ViewModel.UploadLocalHistoryCommand.Execute(Record);
        }
    }

    private void CancelUploadButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (Record != null && ViewModel != null)
        {
            ViewModel.CancelUploadCommand.Execute(Record);
        }
    }

    private async void CopyButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (Record != null && ViewModel != null)
        {
            await ViewModel.HandleCopyButtonAsync(Record, false);
        }
    }

    private async void PasteButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (Record != null && ViewModel != null)
        {
            await ViewModel.HandleCopyButtonAsync(Record, true);
        }
    }

    private void StarButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (Record != null && ViewModel != null)
        {
            ViewModel.ChangeStarStatus(Record);
        }
    }

    private void DeleteButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (Record != null && ViewModel != null)
        {
            ViewModel.DeleteItem(Record);
        }
    }
}
