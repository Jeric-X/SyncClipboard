using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Views;

public partial class HistoryWindow : Window, IWindow
{
    private readonly HistoryViewModel _viewModel;
    private readonly ICaretPositionProvider _caretPositionProvider;
    private readonly ILogger _logger;
    public HistoryViewModel ViewModel => _viewModel;
    private bool _firstShow = true;
    public HistoryWindow()
    {
        _viewModel = App.Current.Services.GetRequiredService<HistoryViewModel>();
        _caretPositionProvider = App.Current.Services.GetRequiredService<ICaretPositionProvider>();
        _logger = App.Current.Services.GetRequiredService<ILogger>();
        var configManager = App.Current.Services.GetRequiredService<ConfigManager>();
        DataContext = ViewModel;

        this.ExtendClientAreaToDecorationsHint = true;
        if (!OperatingSystem.IsMacOS())
            this.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;

        InitializeComponent();
        InitializeScrollWatcher();
        SetWindowMinSize();

        this.Deactivated += (_, _) => _viewModel.OnLostFocus();
        this.Activated += (_, _) =>
        {
            _viewModel.OnGotFocus();
            _SearchTextBox.Focus();
        };

        Height = _viewModel.Height;
        Width = _viewModel.Width;
        this.SizeChanged += (_, _) =>
        {
            _viewModel.Height = (int)Height;
            _viewModel.Width = (int)Width;
        };

        this.Topmost = _viewModel.IsTopmost;
        _ = _viewModel.Init(this);

        // 初始化预览面板布局
        UpdateListViewWidthForPreviewPanel();

        // 监听ViewModel属性变化
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HistoryViewModel.ShowPreviewPanel))
            {
                UpdateListViewWidthForPreviewPanel();
            }
        };
    }

    private void SetWindowMinSize()
    {
        var infiniteSize = new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity);
        _FilterSelectorBar.Measure(infiniteSize);
        _ButtonArea.Measure(infiniteSize);
        _SearchTextBox.Measure(infiniteSize);

        MinWidth = _FilterSelectorBar.DesiredSize.Width + (_ButtonArea.DesiredSize.Width * 2);
        MinHeight = _FilterSelectorBar.DesiredSize.Height + _SearchTextBox.DesiredSize.Height;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _SearchTextBox.Focus();
            _SearchTextBox.SelectAll();
            e.Handled = true;
            return;
        }

        var isShiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var isAltPressed = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var isCtrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        var key = Utilities.KeyboardMap.ConvertFromAvalonia(e.Key);

        if (!key.HasValue)
        {
            _logger.Write($"Avalonia key '{e.Key}' is not supported by KeyboardMap. Please add mapping for this key.");
            return;
        }

        var handled = _viewModel.HandleKeyPress(key.Value, isShiftPressed, isAltPressed, isCtrlPressed);

        if (handled)
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.ApplicationShutdown || e.CloseReason == WindowCloseReason.OSShutdown)
        {
            base.OnClosing(e);
            return;
        }
        this.Hide();
        e.Cancel = true;
    }

    public void SwitchVisible()
    {
        if (!this.IsVisible)
        {
            FocusOnScreen();
        }
        else
        {
            this.Close();
        }
    }

    protected virtual void FocusOnScreen()
    {
        if (!this.IsVisible)
        {
            if (!_viewModel.RepositionWindow() && _firstShow)
            {
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            _firstShow = false;
        }
        else
        {
            _viewModel.RepositionWindow();
        }
        this.Show();
        if (this.WindowState == WindowState.Minimized)
        {
            this.WindowState = WindowState.Normal;
        }
        this.Activate();

        _SearchTextBox.Focus();
        _SearchTextBox.SelectAll();
        _viewModel.OnWindowShown();
    }

    void IWindow.Focus()
    {
        FocusOnScreen();
    }

    public void ScrollToSelectedItem()
    {
        if (_ListBox.SelectedItem != null)
        {
            _ListBox.ScrollIntoView(_ListBox.SelectedItem);
        }
    }

    private void InitializeScrollWatcher()
    {
        if (_ListBox.GetValue(ListBox.ScrollProperty) is ScrollViewer existing)
        {
            AttachScrollViewerWatcher(existing);
            return;
        }

        void handler(object? s, AvaloniaPropertyChangedEventArgs e)
        {
            try
            {
                if (e.Property != ListBox.ScrollProperty) return;
                if (e.NewValue is not ScrollViewer sv) return;

                _ListBox.PropertyChanged -= handler;
                AttachScrollViewerWatcher(sv);
            }
            catch { }
        }

        _ListBox.PropertyChanged += handler;
    }

    private void AttachScrollViewerWatcher(ScrollViewer scroll)
    {
        _scrollViewer = scroll;
        scroll.PropertyChanged += async (_, e) =>
        {
            if (e.Property != ScrollViewer.OffsetProperty && e.Property != ScrollViewer.ViewportProperty && e.Property != ScrollViewer.ExtentProperty)
                return;

            var offsetY = scroll.Offset.Y;
            var viewport = scroll.Viewport.Height;
            var extent = scroll.Extent.Height;

            await ViewModel.NotifyScrollPositionAsync(offsetY, viewport, extent);
        };
    }

    private ScrollViewer? _scrollViewer = null;

    public bool GetScrollViewMetrics(out double offsetY, out double viewportHeight, out double extentHeight)
    {
        offsetY = 0; viewportHeight = 0; extentHeight = 0;
        if (_scrollViewer != null)
        {
            var offset = _scrollViewer.Offset;
            offsetY = offset.Y;
            viewportHeight = _scrollViewer.Viewport.Height;
            extentHeight = _scrollViewer.Extent.Height;
            return true;
        }
        return false;
    }

    private async void ItemContextFlyout_Opening(object? sender, EventArgs e)
    {
        if (sender is not FAMenuFlyout flyout)
        {
            return;
        }

        HistoryRecordVM? record = null;
        if (flyout.Target is Control placement)
        {
            record = placement.DataContext as HistoryRecordVM;
        }

        if (record is null)
        {
            flyout.Items.Clear();
            return;
        }
        _ListBox.SelectedItem = record;

        var actions = await _viewModel.BuildActionsAsync(record);
        flyout.Items.Clear();
        foreach (var action in actions)
        {
            var item = new MenuFlyoutItem { Text = action.Text };
            if (action.Action is not null)
            {
                item.Click += (_, __) => action.Action();
            }
            flyout.Items.Add(item);
        }
    }

    private void PasteButtonClicked(object? sender, RoutedEventArgs e)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is not HistoryRecordVM record)
        {
            return;
        }

        e.Handled = true;
        _ = _viewModel.CopyToClipboard(record, true, CancellationToken.None);
    }

    private void FontScaleMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        this.TryGetResource("FontScaleFlyout", null, out var resource);
        if (resource is Flyout flyout)
        {
            if (flyout.Content is StackPanel panel)
            {
                ((TextBlock)panel.Children[0]).Text = Strings.FontScale;
                ((NumericUpDown)panel.Children[1]).Value = _viewModel.FontScalePercent;
            }
            flyout.ShowAt(_MenuButton);
        }
    }

    private void FontScaleNumericUpDown_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (e.NewValue.HasValue)
            _viewModel.FontScalePercent = (int)Math.Clamp((double)e.NewValue.Value, 25, 400);
    }

    private void CopyButtonClicked(object? sender, RoutedEventArgs e)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is not HistoryRecordVM record)
        {
            return;
        }

        e.Handled = true;
        _ = _viewModel.CopyToClipboard(record, false, CancellationToken.None);
    }

    private void ListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        var history = ((ListBox?)sender)?.SelectedValue;
        if (history is not HistoryRecordVM record)
        {
            return;
        }
        _viewModel.HandleItemDoubleClick(record);
    }

    private void Grid_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        var clickedItem = ((Grid?)sender)?.DataContext as HistoryRecordVM;
        if (clickedItem == null)
        {
            return;
        }

        // 鼠标中键：复制并粘贴
        if (e.Pointer.Type == PointerType.Mouse)
        {
            var properties = e.GetCurrentPoint((Grid?)sender!).Properties;
            if (properties.IsMiddleButtonPressed)
            {
                e.Handled = true;
                _ = _viewModel.CopyToClipboard(clickedItem, true, CancellationToken.None);
                return;
            }

            // 左键：记录起始位置，等待拖拽阈值
            // 不设置 e.Handled = true，让 ListBox 正常处理选择
            if (properties.IsLeftButtonPressed)
            {
                _isPendingDrag = true;
                _dragStartPoint = e.GetPosition(null);
                _pendingDragItem = clickedItem;
                _dragSource = sender as Control;
                e.Pointer.Capture((IInputElement)sender!);
            }
        }
    }

    private async void Grid_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        if (!_isPendingDrag || _pendingDragItem == null || _dragSource == null)
            return;

        var currentPoint = e.GetPosition(null);
        var delta = currentPoint - _dragStartPoint;

        // 检查是否超过拖拽阈值
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            return;

        // 开始拖拽，此时阻止默认行为
        e.Handled = true;
        _isPendingDrag = false;
        var item = _pendingDragItem;
        _pendingDragItem = null;

        // 释放指针捕获并清理状态
        e.Pointer.Capture(null);
        _dragSource = null;

        try
        {
            // Avalonia 11.3+: 使用 DataTransfer API
            var dataTransfer = new DataTransfer();
            var success = await _viewModel.FillDragPackage(dataTransfer, item, CancellationToken.None);
            if (success)
            {
                var result = await DragDrop.DoDragDropAsync(
                    e,
                    dataTransfer,
                    Avalonia.Input.DragDropEffects.Copy);
            }
        }
        catch
        {
            // 拖拽失败，忽略
        }
    }

    private void Grid_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (OperatingSystem.IsLinux())
        {
            return;
        }

        if (_isPendingDrag)
        {
            // 只是点击，没有触发拖拽，释放指针捕获，不阻止事件
            _isPendingDrag = false;
            _pendingDragItem = null;
            _dragSource = null;
            e.Pointer.Capture(null);
            // 不设置 e.Handled = true，让 ListBox 正常处理选择
        }
    }

    private void Image_DoubleTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        var history = ((Image?)sender)?.DataContext;
        if (history is not HistoryRecordVM record)
        {
            return;
        }
        _viewModel.HandleImageDoubleClick(record);
    }

    private void Image_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Image image || image.DataContext is not HistoryRecordVM record)
        {
            return;
        }

        void ImageProperChanged(object? s, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Image.SourceProperty)
            {
                return;
            }
            SetImageVisual(image);
        }
        image.PropertyChanged += ImageProperChanged;

        void OnUnloaded(object? s, RoutedEventArgs e)
        {
            image.Unloaded -= OnUnloaded;
            image.PropertyChanged -= ImageProperChanged;
        }
        image.Unloaded += OnUnloaded;

        if (image.Source is not null)
        {
            SetImageVisual(image);
        }
    }

    private void SetImageVisual(Image image)
    {
        _InvisualableImage.Source = image.Source;
        _InvisualableImage.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _InvisualableImage.Source = null;
        var desiredSize = _InvisualableImage.DesiredSize;

        if (desiredSize.Height > 200)
        {
            image.Stretch = Stretch.Uniform;
        }
        image.Opacity = 1;
    }

    public void ScrollToTop()
    {
        _scrollViewer?.ScrollToHome();
    }

    public void SetTopmost(bool topmost)
    {
        this.Topmost = topmost;
    }

    // 在 macOS 上，ViewModel 的 Width 和 Height 已经是物理像素值，不需要再乘以 RenderScaling
    private (int Width, int Height) GetPhysicalPixelSize()
    {
        if (OperatingSystem.IsMacOS())
        {
            return (_viewModel.Width, _viewModel.Height);
        }
        var scale = this.RenderScaling;
        return ((int)Math.Round(_viewModel.Width * scale), (int)Math.Round(_viewModel.Height * scale));
    }

    public bool SetNearCaretPosition(ScreenPosition caretPosition)
    {
        var screens = Screens.All;
        var screen = screens.FirstOrDefault(s => s.Bounds.Contains(new PixelPoint(caretPosition.X, caretPosition.Y)))
                     ?? Screens.Primary;

        if (screen == null)
        {
            return false;
        }

        var workArea = screen.WorkingArea;
        var (windowWidth, windowHeight) = GetPhysicalPixelSize();

        var (posX, posY) = WindowPositionHelper.CalculateNearCaretPosition(
            caretPosition, windowWidth, windowHeight,
            workArea.X, workArea.Y, workArea.Width, workArea.Height);

        this.WindowStartupLocation = WindowStartupLocation.Manual;
        this.Position = new PixelPoint(posX, posY);
        return true;
    }

    public bool SetNearMousePosition(ScreenPosition mousePosition)
    {
        var screens = Screens.All;
        var screen = screens.FirstOrDefault(s => s.Bounds.Contains(new PixelPoint(mousePosition.X, mousePosition.Y)))
                     ?? Screens.Primary;

        if (screen == null)
        {
            return false;
        }

        var workArea = screen.WorkingArea;
        var (windowWidth, windowHeight) = GetPhysicalPixelSize();

        var (posX, posY) = WindowPositionHelper.CalculateNearMousePosition(
            mousePosition, windowWidth, windowHeight,
            workArea.X, workArea.Y, workArea.Width, workArea.Height);

        this.WindowStartupLocation = WindowStartupLocation.Manual;
        this.Position = new PixelPoint(posX, posY);
        return true;
    }

    public bool SetPositionOnScreen(int screenX, int screenY)
    {
        var screens = Screens.All;
        var targetScreen = screens.FirstOrDefault(s => s.Bounds.Contains(new PixelPoint(screenX, screenY)))
                           ?? Screens.Primary;

        if (targetScreen == null)
        {
            return false;
        }

        if (!_firstShow)
        {
            var currentScreen = screens.FirstOrDefault(s => s.Bounds.Contains(this.Position))
                                ?? Screens.Primary;
            if (currentScreen != null && currentScreen == targetScreen)
            {
                return true;
            }
        }

        var workArea = targetScreen.WorkingArea;
        var (windowWidth, windowHeight) = GetPhysicalPixelSize();

        var (x, y) = WindowPositionHelper.CalculateCenterOnScreenPosition(
            windowWidth, windowHeight,
            workArea.X, workArea.Y, workArea.Width, workArea.Height);

        this.WindowStartupLocation = WindowStartupLocation.Manual;
        this.Position = new PixelPoint(x, y);
        return true;
    }

    private void UpdateListViewWidthForPreviewPanel()
    {
        if (_viewModel.ShowPreviewPanel)
        {
            // 预览面板显示时，ListView固定宽度，预览面板填充剩余空间
            _MainContentGrid.ColumnDefinitions[0].Width = new GridLength(0, GridUnitType.Auto);
            _MainContentGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            _ListBox.Width = _viewModel.ListViewWidth;
        }
        else
        {
            // 预览面板关闭时，ListView填充整个Grid，预览面板列不占空间
            _MainContentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            _MainContentGrid.ColumnDefinitions[2].Width = new GridLength(0, GridUnitType.Auto);
            _ListBox.Width = double.NaN; // Auto
        }
    }

    // 分隔条拖动相关
    private bool _isDraggingSplitter = false;
    private double _splitterStartX = 0;
    private int _splitterStartWidth = 0;

    // 列表项拖拽相关
    private const double DragThreshold = 4; // 拖拽阈值（像素）
    private bool _isPendingDrag = false;
    private Point _dragStartPoint;
    private HistoryRecordVM? _pendingDragItem;
    private Control? _dragSource;

    private void PreviewSplitter_PointerEntered(object sender, PointerEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = new SolidColorBrush(Color.Parse("#0078D4")); // 使用蓝色作为强调色
        }
    }

    private void PreviewSplitter_PointerExited(object sender, PointerEventArgs e)
    {
        if (sender is Border border && !_isDraggingSplitter)
        {
            border.Background = Brushes.LightGray;
        }
    }

    private void PreviewSplitter_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (sender is Border border)
        {
            _isDraggingSplitter = true;
            _splitterStartX = e.GetPosition(_MainContentGrid).X;
            _splitterStartWidth = _viewModel.ListViewWidth;
            e.Pointer.Capture(border);
            e.Handled = true;
        }
    }

    private void PreviewSplitter_PointerMoved(object sender, PointerEventArgs e)
    {
        if (!_isDraggingSplitter)
            return;

        var currentX = e.GetPosition(_MainContentGrid).X;
        var delta = currentX - _splitterStartX;
        var newWidth = _splitterStartWidth + (int)delta;

        // 计算最大宽度：确保预览面板至少300像素，分隔条宽度为1像素
        var maxWidth = (int)_MainContentGrid.Bounds.Width - 300 - 1;
        if (maxWidth < 150) maxWidth = 150;

        // 限制宽度范围
        newWidth = Math.Clamp(newWidth, 150, maxWidth);

        _viewModel.ListViewWidth = newWidth;
        _ListBox.Width = newWidth;
        e.Handled = true;
    }

    private void PreviewSplitter_PointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (sender is Border border)
        {
            _isDraggingSplitter = false;
            e.Pointer.Capture(null);
            border.Background = Brushes.LightGray;
        }
    }
}