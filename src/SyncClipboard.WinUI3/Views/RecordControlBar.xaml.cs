using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;
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
        if (Record is not { } record || ViewModel is null || !IsLoaded || XamlRoot is not { } root)
            return;

        var key = record.Key;
        _AddTagButton.IsEnabled = false;
        try
        {
            var picker = await CreateTagPickerAsync(key, ViewModel.PinnedTags);
            if (picker is null || Record?.Key != key || !IsLoaded || Visibility != Visibility.Visible || XamlRoot != root)
                return;
            ShowTagPicker(key, picker);
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

    private static async Task<HistoryTagPickerViewModel?> CreateTagPickerAsync(HistoryRecordKey key, string[] pinnedNames)
    {
        var manager = App.Current.Services.GetRequiredService<HistoryManager>();
        var snapshot = await Task.Run(() => manager.GetSyncSnapshotAsync(key));
        if (snapshot is null || snapshot.IsDeleted)
            return null;
        var counts = await Task.Run(() => manager.GetTagCountsAsync());
        return new HistoryTagPickerViewModel(counts.Keys, snapshot.Tags, (name, add) => Task.Run(async () =>
        {
            await manager.EditTagsAsync([key], add ? [name] : [], add ? [] : [name]);
            if (await manager.GetSyncSnapshotAsync(key) is not { IsDeleted: false })
                throw new InvalidOperationException(Strings.HistoryTagsNoSelection);
        }), pinnedNames);
    }

    private void ShowTagPicker(HistoryRecordKey key, HistoryTagPickerViewModel picker)
    {
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
        };
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
