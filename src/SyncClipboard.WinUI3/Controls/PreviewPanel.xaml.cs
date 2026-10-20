using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SyncClipboard.Core;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.WinUI3.Utilities;
using System;
using System.ComponentModel;
using Windows.ApplicationModel.DataTransfer;

namespace SyncClipboard.WinUI3.Controls;

/// <summary>
/// 预览面板控件，用于显示剪贴板记录的预览内容
/// </summary>
public sealed partial class PreviewPanel : UserControl
{
    /// <summary>
    /// ViewModel依赖属性
    /// </summary>
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(HistoryViewModel),
            typeof(PreviewPanel),
            new PropertyMetadata(null, OnViewModelChanged));

    /// <summary>
    /// SelectedItem依赖属性
    /// </summary>
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(HistoryRecordVM),
            typeof(PreviewPanel),
            new PropertyMetadata(null, OnSelectedItemChanged));

    public HistoryViewModel ViewModel
    {
        get => (HistoryViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public HistoryRecordVM SelectedItem
    {
        get => (HistoryRecordVM)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public PreviewPanel()
    {
        this.InitializeComponent();
        PreviewImageLoader.GetState(_PreviewImage).FileMissing += PreviewImage_FileMissing;
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PreviewPanel panel)
            return;

        if (e.OldValue is HistoryViewModel oldViewModel)
            oldViewModel.PropertyChanged -= panel.ViewModel_PropertyChanged;
        if (e.NewValue is HistoryViewModel viewModel)
            viewModel.PropertyChanged += panel.ViewModel_PropertyChanged;

        panel.UpdatePreviewImageContent();
        panel.UpdateStatusText();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.EnableSyncHistory))
            UpdateStatusText();
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PreviewPanel panel)
        {
            // 取消订阅旧选中项的事件
            if (e.OldValue is HistoryRecordVM oldRecord)
            {
                oldRecord.PropertyChanged -= panel.SelectedItem_PropertyChanged;
            }

            // 订阅新选中项的事件
            if (e.NewValue is HistoryRecordVM newRecord)
            {
                newRecord.PropertyChanged += panel.SelectedItem_PropertyChanged;
            }

            panel.UpdatePreviewImageContent();
            panel.UpdateStatusText();
        }
    }

    private void SelectedItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryRecordVM.PreviewImage))
        {
            UpdatePreviewImageContent();
        }

        if (e.PropertyName is nameof(HistoryRecordVM.SyncState) or nameof(HistoryRecordVM.IsLocalFileReady))
        {
            UpdateStatusText();
        }
    }

    internal void StatusTextLoaded(object _, RoutedEventArgs _1)
    {
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (ViewModel == null || _StatusText == null)
            return;

        var isLocalFileMissing = SelectedItem is
        {
            SyncState: SyncStatus.LocalOnly,
            IsLocalFileReady: false
        };
        var isSynced = ViewModel.EnableSyncHistory && SelectedItem?.SyncState == SyncStatus.Synced;

        _StatusText.Text = isLocalFileMissing ? Strings.FileMissing : Strings.Synced;
        _StatusText.Visibility = isLocalFileMissing || isSynced ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePreviewImageContent()
    {
        PreviewImageLoader.SetFilePath(_PreviewImage,
            SelectedItem?.Type == ProfileType.Image ? SelectedItem.PreviewImage : null);
    }

    private async void PreviewImage_FileMissing(object? sender, EventArgs e)
    {
        var record = SelectedItem;
        var viewModel = ViewModel;
        if (record is null || viewModel is null)
            return;

        try
        {
            await viewModel.MarkLocalFileMissingAsync(record);
        }
        catch (Exception ex)
        {
            AppCore.TryGetCurrent()?.Logger.Write(nameof(PreviewPanel), $"Failed to mark missing image: {ex.Message}");
        }
    }

    internal void PreviewImage_DoubleTapped(object _, DoubleTappedRoutedEventArgs _1)
    {
        if (SelectedItem is HistoryRecordVM record && record.Type == ProfileType.Image && record.PreviewImage != null)
        {
            ViewModel?.ViewImage(record);
        }
    }

    internal async void PreviewImage_DragStarting(UIElement _, DragStartingEventArgs e)
    {
        if (SelectedItem is not HistoryRecordVM record)
        {
            e.Cancel = true;
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            e.Data.RequestedOperation = DataPackageOperation.Copy;
            await DragUiHelper.SetDragIconAsync(e.DragUI, record);
            var success = await ViewModel.FillDragPackage(e.Data, record);

            if (!success)
            {
                e.Cancel = true;
            }
        }
        catch (Exception ex)
        {
            AppCore.TryGetCurrent()?.Logger.Write(nameof(PreviewPanel), $"Drag operation failed: {ex.Message}");
            e.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
