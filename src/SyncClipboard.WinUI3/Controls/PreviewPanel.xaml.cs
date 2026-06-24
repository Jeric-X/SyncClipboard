using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SyncClipboard.Core;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.WinUI3.ValueConverters;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;

namespace SyncClipboard.WinUI3.Controls;

/// <summary>
/// 预览面板控件，用于显示剪贴板记录的预览内容
/// </summary>
public sealed partial class PreviewPanel : UserControl
{
    private (uint width, uint height) _currentPreviewImageSize = (0, 0);

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
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PreviewPanel panel && e.NewValue is HistoryViewModel viewModel)
        {
            // 监听ViewModel的ShowPreviewPanel变化
            viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(HistoryViewModel.ShowPreviewPanel))
                {
                    // 打开预览面板时，立即更新图片预览内容
                    if (viewModel.ShowPreviewPanel)
                    {
                        _ = panel.UpdatePreviewImageContent();
                    }
                }
                else if (args.PropertyName == nameof(HistoryViewModel.EnableSyncHistory))
                {
                    panel.UpdateSyncedTextVisibility();
                }
            };
        }
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

            _ = panel.UpdatePreviewImageContent();
            panel.UpdateSyncedTextVisibility();
        }
    }

    private void SelectedItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryRecordVM.PreviewImage))
        {
            _ = UpdatePreviewImageContent();
        }
    }

    internal void SyncedTextLoaded(object _, RoutedEventArgs _1)
    {
        UpdateSyncedTextVisibility();
    }

    private void UpdateSyncedTextVisibility()
    {
        if (ViewModel == null || _SyncedText == null)
            return;

        var enableSync = ViewModel.EnableSyncHistory;
        var isSynced = SelectedItem?.SyncState == SyncStatus.Synced;

        _SyncedText.Visibility = enableSync && isSynced ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 只处理图片预览的异步加载，文字预览通过绑定自动更新
    /// </summary>
    private async Task UpdatePreviewImageContent()
    {
        if (ViewModel?.ShowPreviewPanel != true)
            return;

        // 清空之前的图片预览
        _PreviewImage.Source = null;
        _currentPreviewImageSize = (0, 0);

        // 检查是否选中了记录
        if (SelectedItem is not HistoryRecordVM record)
            return;

        // 只有图片类型且有预览图片时才异步加载
        if (record.Type != ProfileType.Image || record.PreviewImage == null)
            return;

        try
        {
            var (width, height) = await GetImageDimensions(record.PreviewImage);

            // 检查当前选中项是否还是同一个记录
            if (SelectedItem != record)
                return;

            // 保存图片尺寸，用于面板大小变化时重新计算
            _currentPreviewImageSize = (width, height);

            // 计算并设置 Stretch
            UpdatePreviewImageStretch();

            // 设置图片源
            _PreviewImage.Source = ConvertMethod.CreateBitmap(record.PreviewImage);
        }
        catch (Exception ex)
        {
            AppCore.TryGetCurrent()?.Logger.Write(nameof(PreviewPanel), $"Failed to get image dimensions: {ex.Message}");

            // 检查当前选中项是否还是同一个记录
            if (SelectedItem != record)
                return;

            _currentPreviewImageSize = (0, 0);
            _PreviewImage.Stretch = Stretch.Uniform;
            _PreviewImage.Source = ConvertMethod.CreateBitmap(record.PreviewImage);
        }
    }

    internal void PreviewPanel_SizeChanged(object _, SizeChangedEventArgs _1)
    {
        // 预览面板大小变化时，重新计算图片 Stretch
        if (_currentPreviewImageSize.width > 0 && _currentPreviewImageSize.height > 0)
        {
            UpdatePreviewImageStretch();
        }
    }

    private void UpdatePreviewImageStretch()
    {
        var (width, height) = _currentPreviewImageSize;
        if (width == 0 || height == 0)
            return;

        // 获取预览面板的实际大小
        var panelWidth = ActualWidth - 24;
        var panelHeight = ActualHeight - 48; // 减去头部高度

        // 根据图片大小决定 Stretch
        if (width > panelWidth || height > panelHeight)
        {
            _PreviewImage.Stretch = Stretch.Uniform;
        }
        else
        {
            _PreviewImage.Stretch = Stretch.None;
        }
    }

    private static async Task<(uint width, uint height)> GetImageDimensions(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
        return (decoder.PixelWidth, decoder.PixelHeight);
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

        try
        {
            e.Data.RequestedOperation = DataPackageOperation.Copy;
            var success = await ViewModel.FillDragPackage(e.Data, record, CancellationToken.None);
            e.DragUI.SetContentFromDataPackage();

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
    }
}