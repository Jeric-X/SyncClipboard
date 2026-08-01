using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class RecordControlBar : UserControl
{
    public HistoryViewModel? ViewModel
    {
        get => (HistoryViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(HistoryViewModel), typeof(RecordControlBar), new PropertyMetadata(null));

    public HistoryRecordVM? Record
    {
        get => (HistoryRecordVM?)GetValue(RecordProperty);
        set => SetValue(RecordProperty, value);
    }

    public static readonly DependencyProperty RecordProperty =
        DependencyProperty.Register(nameof(Record), typeof(HistoryRecordVM), typeof(RecordControlBar), new PropertyMetadata(null));

    public RecordControlBar()
    {
        InitializeComponent();
    }

    private void DownloadButtonClicked(object _, RoutedEventArgs _1)
    {
        if (Record != null && ViewModel != null)
        {
            ViewModel.DownloadRemoteProfileCommand.Execute(Record);
        }
    }

    private void CancelDownloadButtonClicked(object _, RoutedEventArgs _1)
    {
        if (Record != null && ViewModel != null)
        {
            ViewModel.CancelDownloadCommand.Execute(Record);
        }
    }

    private void UploadButtonClicked(object _, RoutedEventArgs _1)
    {
        if (Record != null && ViewModel != null)
        {
            ViewModel.UploadLocalHistoryCommand.Execute(Record);
        }
    }

    private void CancelUploadButtonClicked(object _, RoutedEventArgs _1)
    {
        if (Record != null && ViewModel != null)
        {
            ViewModel.CancelUploadCommand.Execute(Record);
        }
    }

    private async void CopyButtonClicked(object _, RoutedEventArgs _1)
    {
        if (Record != null && ViewModel != null)
        {
            await ViewModel.HandleCopyButtonAsync(Record, false);
        }
    }

    private async void PasteButtonClicked(object _, RoutedEventArgs _1)
    {
        if (Record != null && ViewModel != null)
        {
            await ViewModel.HandleCopyButtonAsync(Record, true);
        }
    }

    private void StarButtonClicked(object _, RoutedEventArgs _1)
    {
        if (Record != null && ViewModel != null)
        {
            ViewModel.ChangeStarStatus(Record);
        }
    }

    private void DeleteButtonClicked(object _, RoutedEventArgs _1)
    {
        if (Record != null && ViewModel != null)
        {
            ViewModel.DeleteItem(Record);
        }
    }
}
