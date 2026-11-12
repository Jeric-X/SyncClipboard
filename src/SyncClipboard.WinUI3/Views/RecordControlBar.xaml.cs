using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using System;
using System.Threading.Tasks;

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

    private async void AddTagButtonClicked(object _, RoutedEventArgs _1)
    {
        if (Record is not { } record || ViewModel is null || ViewModel.HasModalOperation || !IsLoaded || XamlRoot is not { } root)
            return;

        using var hold = ViewModel.HoldForModalOperation();
        var key = record.Key;
        _AddTagButton.IsEnabled = false;
        try
        {
            var picker = await ViewModel.CreateTagPickerAsync(key);
            if (picker is null || Record?.Key != key || !IsLoaded || Visibility != Visibility.Visible || XamlRoot != root)
                return;
            await ShowTagPickerAsync(key, picker);
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = Strings.HistoryTagsQuickAdd,
                Content = new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = Strings.Confirm,
                XamlRoot = root
            }.ShowAsync();
        }
        finally
        {
            _AddTagButton.IsEnabled = true;
        }
    }

    private Task ShowTagPickerAsync(HistoryRecordKey key, HistoryTagPickerViewModel picker)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flyout = new HistoryTagPickerView(picker).ShowAt(_AddTagButton);
        var callback = RegisterPropertyChangedCallback(RecordProperty, (_, _) =>
        {
            if (Record?.Key != key)
                flyout.Hide();
        });
        void Unload(object _, RoutedEventArgs _1) => flyout.Hide();
        Unloaded += Unload;
        flyout.Closed += (_, _) =>
        {
            UnregisterPropertyChangedCallback(RecordProperty, callback);
            Unloaded -= Unload;
            closed.TrySetResult();
        };
        return closed.Task;
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
