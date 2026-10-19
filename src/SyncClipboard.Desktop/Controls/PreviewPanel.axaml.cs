using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SyncClipboard.Core;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Desktop.Utilities;
using System;
using System.ComponentModel;

namespace SyncClipboard.Desktop.Controls;

/// <summary>
/// 预览面板控件，用于显示剪贴板记录的预览内容
/// </summary>
public sealed partial class PreviewPanel : UserControl
{
    private const double DragThreshold = 4; // 拖拽阈值（像素）
    private bool _isPendingDrag = false;
    private Point _dragStartPoint;
    private HistoryRecordVM? _pendingDragItem;
    private Control? _dragSource;
    private IPointer? _dragPointer;
    private PointerPressedEventArgs? _dragStartEventArgs;

    /// <summary>
    /// ViewModel依赖属性
    /// </summary>
    public static readonly StyledProperty<HistoryViewModel> ViewModelProperty =
        AvaloniaProperty.Register<PreviewPanel, HistoryViewModel>(nameof(ViewModel));

    /// <summary>
    /// SelectedItem依赖属性
    /// </summary>
    public static readonly StyledProperty<HistoryRecordVM> SelectedItemProperty =
        AvaloniaProperty.Register<PreviewPanel, HistoryRecordVM>(nameof(SelectedItem));

    public HistoryViewModel ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public HistoryRecordVM SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public PreviewPanel()
    {
        InitializeComponent();
        _PreviewImage.FileMissing += PreviewImage_FileMissing;

        // 监听属性变化
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ViewModelProperty)
        {
            if (e.OldValue is HistoryViewModel oldViewModel)
            {
                oldViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
            if (e.NewValue is HistoryViewModel viewModel)
            {
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
            UpdatePreviewImageContent();
        }
        else if (e.Property == SelectedItemProperty)
        {
            // 取消订阅旧选中项的事件
            if (e.OldValue is HistoryRecordVM oldRecord)
            {
                oldRecord.PropertyChanged -= SelectedItem_PropertyChanged;
            }

            // 订阅新选中项的事件
            if (e.NewValue is HistoryRecordVM newRecord)
            {
                newRecord.PropertyChanged += SelectedItem_PropertyChanged;
            }

            UpdatePreviewImageContent();
            UpdateStatusText();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.EnableSyncHistory))
        {
            UpdateStatusText();
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

    private void UpdatePreviewImageContent()
    {
        _PreviewImage.FilePath = SelectedItem?.Type == ProfileType.Image
            ? SelectedItem.PreviewImage
            : null;
    }

    private async void PreviewImage_FileMissing(object? sender, EventArgs e)
    {
        var record = SelectedItem;
        var viewModel = ViewModel;
        if (record is null || viewModel is null)
        {
            return;
        }
        try
        {
            await viewModel.MarkLocalFileMissingAsync(record);
        }
        catch (Exception ex)
        {
            AppCore.TryGetCurrent()?.Logger.Write("PreviewPanel", $"Failed to mark missing image: {ex.Message}");
        }
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
        _StatusText.IsVisible = isLocalFileMissing || isSynced;
    }

    private void PreviewImage_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SelectedItem is HistoryRecordVM record && record.Type == ProfileType.Image && record.PreviewImage != null)
        {
            ViewModel?.ViewImage(record);
        }
    }

    private void PreviewImage_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        var clickedItem = SelectedItem;
        if (clickedItem == null)
        {
            return;
        }

        // 鼠标左键：记录起始位置，等待拖拽阈值
        if (e.Pointer.Type == PointerType.Mouse)
        {
            var properties = e.GetCurrentPoint((Image?)sender!).Properties;
            if (properties.IsLeftButtonPressed)
            {
                ResetPendingDrag();
                _isPendingDrag = true;
                _dragStartPoint = e.GetPosition(null);
                _pendingDragItem = clickedItem;
                _dragSource = sender as Control;
                _dragPointer = e.Pointer;
                _dragStartEventArgs = e;
                e.Pointer.Capture((IInputElement)sender!);
            }
        }
    }

    private async void PreviewImage_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        if (!_isPendingDrag || _pendingDragItem == null || _dragSource == null || _dragStartEventArgs == null || ViewModel == null)
            return;

        var currentPoint = e.GetPosition(null);
        var delta = currentPoint - _dragStartPoint;

        // 检查是否超过拖拽阈值
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            return;

        // 开始拖拽，此时阻止默认行为
        e.Handled = true;
        var item = _pendingDragItem;
        var dragStartEventArgs = _dragStartEventArgs;
        ResetPendingDrag();

        await AvaloniaDragDropHelper.DoDragDropAsync(dragStartEventArgs, data => ViewModel.FillDragPackage(data, item));
    }

    private void PreviewImage_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        ResetPendingDrag();
    }

    internal void ResetPendingDrag()
    {
        _dragPointer?.Capture(null);
        _dragPointer = null;
        _dragStartEventArgs = null;
        _isPendingDrag = false;
        _pendingDragItem = null;
        _dragSource = null;
    }
}
