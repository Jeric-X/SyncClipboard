using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Desktop.Services;
using System;
using System.Threading.Tasks;

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

    private async void AddTagButtonClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (Record is not { } record || ViewModel is null || ViewModel.HasModalOperation || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        using var hold = ViewModel.HoldForModalOperation();
        var key = record.Key;
        _AddTagButton.IsEnabled = false;
        try
        {
            var picker = await ViewModel.CreateTagPickerAsync(key);
            if (picker is null || Record?.Key != key || !IsEffectivelyVisible || !owner.IsVisible || TopLevel.GetTopLevel(this) != owner)
                return;
            await ShowTagPickerAsync(key, picker);
        }
        catch (Exception ex)
        {
            await new AvaloniaDialog(owner).ShowMessageAsync(Strings.HistoryTagsQuickAdd, ex.Message);
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
        void RecordChanged(object? _, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == RecordControlBar.RecordProperty && Record?.Key != key)
                flyout.Hide();
        }
        void Detached(object? _, VisualTreeAttachmentEventArgs __) => flyout.Hide();
        PropertyChanged += RecordChanged;
        DetachedFromVisualTree += Detached;
        flyout.Closed += (_, _) =>
        {
            PropertyChanged -= RecordChanged;
            DetachedFromVisualTree -= Detached;
            closed.TrySetResult();
        };
        return closed.Task;
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
