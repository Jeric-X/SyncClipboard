using CommunityToolkit.WinUI.Converters;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.WinUI3.ValueConverters;
using SyncClipboard.WinUI3.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.System;
using Windows.UI.Core;
using WinUIEx;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HistoryWindow : Window, IWindow
{
    private readonly HistoryViewModel _viewModel;
    public HistoryViewModel ViewModel => _viewModel;
    private bool _windowLoaded = false;
    private readonly MultiTimesEventSimulator _historyItemEvents = new(TimeSpan.FromMilliseconds(300));
    private readonly MultiTimesEventSimulator _imageClickEvents = new(TimeSpan.FromMilliseconds(300));
    private readonly WindowManager _windowManger;
    private ScrollViewer? _scrollViewer = null;
    private readonly ICaretPositionProvider _caretPositionProvider;
    private readonly ILogger _logger;

    public HistoryWindow(ConfigManager configManager, HistoryViewModel viewModel, ICaretPositionProvider caretPositionProvider, ILogger logger)
    {
        _viewModel = viewModel;
        _windowManger = WindowManager.Get(this);
        _caretPositionProvider = caretPositionProvider;
        _logger = logger;
        this.ResizeDip(_viewModel.Width, _viewModel.Height);

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_TitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        this.SetTitleBarButtonForegroundColor();
        _TitleBar.Loaded += (_, _) => SetNonClientPointerSource();
        _FilterSelectorBar.Loaded += (_, _) => DisableSelectorBarScrollBars();

        this.SizeChanged += HistoryWindow_SizeChanged;

        configManager.GetAndListenConfig<ProgramConfig>(config => this.SetTheme(config.Theme));

        this.Closed += OnHistoryWindowClosed;

        this.Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                _viewModel.OnLostFocus();
            }
            else
            {
                _viewModel.OnGotFocus();
                _SearchTextBox.Focus(FocusState.Programmatic);
            }
        };

        _imageClickEvents[2] += () =>
        {
            if (_ListView.SelectedValue is not HistoryRecordVM record)
            {
                return;
            }
            _viewModel.HandleImageDoubleClick(record);
        };

        _historyItemEvents[2] += () =>
        {
            if (_ListView.SelectedValue is not HistoryRecordVM record)
            {
                return;
            }
            _viewModel.HandleItemDoubleClick(record);
        };

        // 初始化 SelectorBar 选项
        InitializeSelectorBar();

        _ListView.SizeChanged += OnListViewSizeChanged;

        InitializeScrollWatcher();

        this.SetTopmost(_viewModel.IsTopmost);

        ApplyFontScale(_viewModel.FontScalePercent);
        ApplyCompactListMaxLines(_viewModel.CompactListMaxLines);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateListViewWidthForPreview(); // 初始化时设置ListView宽度
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.FontScalePercent))
        {
            DispatcherQueue.TryEnqueue(() => ApplyFontScale(_viewModel.FontScalePercent));
        }
        else if (e.PropertyName == nameof(HistoryViewModel.ShowPreviewPanel))
        {
            DispatcherQueue.TryEnqueue(() => UpdateListViewWidthForPreview());
        }
        else if (e.PropertyName == nameof(HistoryViewModel.ListViewWidth) && _viewModel.ShowPreviewPanel)
        {
            DispatcherQueue.TryEnqueue(() => _ListView.Width = _viewModel.ListViewWidth);
        }
        else if (e.PropertyName == nameof(HistoryViewModel.CompactListMaxLines))
        {
            DispatcherQueue.TryEnqueue(() => ApplyCompactListMaxLines(_viewModel.CompactListMaxLines));
        }
    }

    private void ApplyCompactListMaxLines(int maxLines)
    {
        ((CompactListProxy)_HistoryWindowGrid.Resources[nameof(CompactListProxy)]).SetMaxLines(maxLines);

        // 切换模式时更新图片可见性
        UpdateImageVisibilityInListView(maxLines == 0);
    }

    private void UpdateImageVisibilityInListView(bool visible)
    {
        // 遍历 ListView 的可视化树，找到所有 Image 元素并设置可见性
        for (int i = 0; i < _ListView.Items.Count; i++)
        {
            var container = _ListView.ContainerFromIndex(i) as ListViewItem;
            if (container != null)
            {
                var image = FindImageInContainer(container);
                if (image != null && image.Source != null)
                {
                    image.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }

    private Image? FindImageInContainer(DependencyObject container)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(container);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(container, i);
            if (child is Image image)
            {
                return image;
            }
            var found = FindImageInContainer(child);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private void UpdateListViewWidthForPreview()
    {
        if (_viewModel.ShowPreviewPanel)
        {
            // 预览面板显示时，ListView固定宽度，预览面板填充剩余空间
            _MainContentGrid.ColumnDefinitions[0].Width = new GridLength(0, GridUnitType.Auto);
            _MainContentGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            _ListView.Width = _viewModel.ListViewWidth;
        }
        else
        {
            // 预览面板关闭时，ListView填充整个Grid，预览面板列不占空间
            _MainContentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            _MainContentGrid.ColumnDefinitions[2].Width = new GridLength(0, GridUnitType.Auto);
            _ListView.Width = double.NaN; // Auto
        }
    }

    private void ApplyFontScale(int scalePercent)
    {
        ((FontScaleProxy)_HistoryWindowGrid.Resources[nameof(FontScaleProxy)]).SetScale(scalePercent / 100.0);
    }

    private void FontScaleMenuItem_Click(object _0, RoutedEventArgs _1)
    {
        ((Flyout)_HistoryWindowGrid.Resources["FontScaleFlyout"]).ShowAt(_MenuButton);
    }

    private void FontScaleFlyout_Opening(object sender, object _)
    {
        var panel = (StackPanel)((Flyout)sender).Content;
        ((TextBlock)panel.Children[0]).Text = Strings.FontScale;
        ((NumberBox)panel.Children[1]).Value = _viewModel.FontScalePercent;
    }

    private void FontScaleNumberBox_ValueChanged(NumberBox _, NumberBoxValueChangedEventArgs args)
    {
        if (!double.IsNaN(args.NewValue))
        {
            _viewModel.FontScalePercent = (int)Math.Clamp(args.NewValue, 25, 400);
        }
    }

    private void HistoryWindow_SizeChanged(object sender, Microsoft.UI.Xaml.WindowSizeChangedEventArgs args)
    {
        if (_windowLoaded)
        {
            var centerX = this.AppWindow.Position.X + (this.AppWindow.Size.Width / 2);
            var centerY = this.AppWindow.Position.Y + (this.AppWindow.Size.Height / 2);
            var (width, height) = WindowExtention.PhysicalToDip(
                this.AppWindow.Size.Width, this.AppWindow.Size.Height,
                centerX, centerY);
            _viewModel.Width = width;
            _viewModel.Height = height;

            // 如果预览面板显示，确保预览面板至少300宽度
            if (_viewModel.ShowPreviewPanel)
            {
                var maxWidth = (int)_MainContentGrid.ActualWidth - 300 - 2;
                if (maxWidth < 150) maxWidth = 150;
                if (_viewModel.ListViewWidth > maxWidth)
                {
                    _viewModel.ListViewWidth = maxWidth;
                    _ListView.Width = maxWidth;
                }
            }
        }
        SetNonClientPointerSource();
    }

    private void OnHistoryWindowClosed(object sender, WindowEventArgs args)
    {
        this.AppWindow.Hide();
        args.Handled = true;
    }

    private void ShowWindow()
    {
        if (!_windowLoaded)
        {
            SetWindowMinSize();
            _ = _viewModel.Init(this);
        }

        if (!_viewModel.RepositionWindow() && !_windowLoaded)
        {
            this.CenterOnScreenDip(_viewModel.Width, _viewModel.Height);
        }
        this.Activate();
        this.SetForegroundWindow();

        _viewModel.OnWindowShown();
        _SearchTextBox.Focus(FocusState.Programmatic);
        _SearchTextBox.SelectAll();

        if (!_windowLoaded)
        {
            _windowLoaded = true;
        }
    }

    public void Focus()
    {
        if (!this.Visible)
        {
            ShowWindow();
        }
        else
        {
            _viewModel.RepositionWindow();
            this.SetForegroundWindow();
        }
    }

    public void SwitchVisible()
    {
        if (!this.Visible)
        {
            ShowWindow();
        }
        else
        {
            this.AppWindow.Hide();
        }
    }

    private async void PasteButtonClicked(object sender, RoutedEventArgs _)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is HistoryRecordVM record)
        {
            await _viewModel.CopyToClipboard(record, true, CancellationToken.None);
        }
    }

    private void DownloadButtonClicked(object sender, RoutedEventArgs _)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is HistoryRecordVM record)
        {
            _viewModel.DownloadRemoteProfileCommand.Execute(record);
        }
    }

    private void CancelDownloadButtonClicked(object sender, RoutedEventArgs _)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is HistoryRecordVM record)
        {
            _viewModel.CancelDownloadCommand.Execute(record);
        }
    }

    private void DeleteButtonClicked(object sender, RoutedEventArgs _)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is HistoryRecordVM record)
        {
            _viewModel.DeleteItem(record);
        }
    }

    private void OnListViewSizeChanged(object _, SizeChangedEventArgs e)
    {
        // 动态计算InfoBar的位置，距离底部20%高度
        var listViewHeight = e.NewSize.Height;
        var bottomMargin = listViewHeight * 0.2;
        _InfoBar.Margin = new Thickness(0, 0, 0, bottomMargin);
    }

    private void StarButtonClicked(object sender, RoutedEventArgs _)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is HistoryRecordVM record)
        {
            _viewModel.ChangeStarStatus(record);
        }
    }

    private void UploadButtonClicked(object sender, RoutedEventArgs _)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is HistoryRecordVM record)
        {
            _viewModel.UploadLocalHistoryCommand.Execute(record);
        }
    }

    private void CancelUploadButtonClicked(object sender, RoutedEventArgs _)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is HistoryRecordVM record)
        {
            _viewModel.CancelUploadCommand.Execute(record);
        }
    }

    private void CopyButtonClicked(object sender, RoutedEventArgs _)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is HistoryRecordVM record)
        {
            var _1 = _viewModel.CopyToClipboard(record, false, CancellationToken.None);
        }
    }

    private void Grid_KeyDown(object _, KeyRoutedEventArgs e)
    {
        var isCtrlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (e.Key == VirtualKey.F && isCtrlPressed)
        {
            _SearchTextBox.Focus(FocusState.Programmatic);
            _SearchTextBox.SelectAll();
            e.Handled = true;
            return;
        }

        var isShiftPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        var isAltPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down);

        var key = KeyboardMap.ConvertFromVirtualKey(e.Key);

        if (!key.HasValue)
        {
            _logger.Write($"WinUI3 VirtualKey '{e.Key}' is not supported by KeyboardMap. Please add mapping for this key.");
            return;
        }

        var handled = _viewModel.HandleKeyPress(key.Value, isShiftPressed, isAltPressed, isCtrlPressed);

        e.Handled = handled;
    }

    public void ScrollToSelectedItem()
    {
        if (_ListView.SelectedItem != null)
        {
            _ListView.ScrollIntoView(_ListView.SelectedItem);
        }
    }

    private void Image_ImageOpened(object sender, RoutedEventArgs _)
    {
        if (sender is not Image image)
        {
            return;
        }

        // 紧凑模式下不显示图片
        var compactProxy = (CompactListProxy)_HistoryWindowGrid.Resources[nameof(CompactListProxy)];
        if (compactProxy.IsCompact)
        {
            image.Visibility = Visibility.Collapsed;
            return;
        }

        _InvisualableImage.Source = image.Source;
        _InvisualableImage.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _InvisualableImage.Source = null;
        var desiredSize = _InvisualableImage.DesiredSize;

        if (desiredSize.Height > 200)
        {
            image.Stretch = Stretch.Uniform;
        }
        image.Visibility = Visibility.Visible;
    }

    private void Grid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // 如果事件来自 Image，则跳过处理（Image 有自己的 PointerPressed 处理）
        if (e.OriginalSource is Image)
        {
            return;
        }

        e.Handled = false;
        var clickedItem = (HistoryRecordVM)((Grid?)sender)?.DataContext!;
        if ((HistoryRecordVM?)_ListView.SelectedValue != clickedItem)
        {
            _ListView.SelectedValue = clickedItem;
            _historyItemEvents.Reset();
        }

        // 鼠标中键：复制并粘贴
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse)
        {
            var properties = e.GetCurrentPoint(sender as UIElement).Properties;
            if (properties?.IsMiddleButtonPressed == true)
            {
                _ = _viewModel.CopyToClipboard(clickedItem, true, CancellationToken.None);
                return;
            }
        }

        _historyItemEvents.TriggerOriginalEvent();
    }

    private async void Item_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
        var draggedItem = (HistoryRecordVM)((FrameworkElement)sender).DataContext;
        if (draggedItem == null)
        {
            e.Cancel = true;
            return;
        }

        try
        {
            e.Data.RequestedOperation = DataPackageOperation.Copy;
            var success = await _viewModel.FillDragPackage(e.Data, draggedItem, CancellationToken.None);
            e.DragUI.SetContentFromDataPackage();

            if (!success)
            {
                e.Cancel = true;
            }
        }
        catch (Exception ex)
        {
            AppCore.Current.Logger.Write($"Drag operation failed: {ex.Message}");
            e.Cancel = true;
        }
    }

    private void Image_PointerPressed(object sender, PointerRoutedEventArgs _)
    {
        var clickedItem = (HistoryRecordVM)((Image?)sender)?.DataContext!;
        if ((HistoryRecordVM?)_ListView.SelectedValue != clickedItem)
        {
            _ListView.SelectedValue = clickedItem;
            _imageClickEvents.Reset();
        }
        _imageClickEvents.TriggerOriginalEvent();
    }

    public void ScrollToTop()
    {
        _scrollViewer?.ScrollToVerticalOffset(0);
    }

    private async void ItemContextFlyout_Opening(object sender, object _)
    {
        if (sender is not MenuFlyout flyout)
        {
            return;
        }

        if (_ListView.SelectedValue is not HistoryRecordVM record)
        {
            flyout.Items.Clear();
            return;
        }

        flyout.Items.Clear();
        var actions = await _viewModel.BuildActionsAsync(record);
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

    private void SelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs _)
    {
        _viewModel.SelectedFilter = ((LocaleString<HistoryFilterType>)sender.SelectedItem.DataContext).Key;
    }

    private void InitializeSelectorBar()
    {
        _FilterSelectorBar.Items.Clear();

        foreach (var option in _viewModel.FilterOptions)
        {
            var item = new SelectorBarItem
            {
                Text = option.ShownString,
                DataContext = option,
                IsTabStop = false,
            };
            _FilterSelectorBar.Items.Add(item);
        }

        _FilterSelectorBar.SelectedItem = _FilterSelectorBar.Items[(int)_viewModel.SelectedFilter];

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.SelectedFilter))
            {
                UpdateSelectorBarSelection();
            }
        };
    }

    private void InitializeScrollWatcher()
    {
        if (_ListView.IsLoaded)
        {
            var sv = FindDescendant<ScrollViewer>(_ListView);
            if (sv != null)
            {
                AttachScrollViewerWatcher(sv);
                return;
            }
        }

        void onLoaded(object s, RoutedEventArgs e)
        {
            _ListView.Loaded -= onLoaded;
            var sv = FindDescendant<ScrollViewer>(_ListView);
            if (sv != null)
            {
                AttachScrollViewerWatcher(sv);
            }
        }

        _ListView.Loaded += onLoaded;
    }

    private void AttachScrollViewerWatcher(ScrollViewer scroll)
    {
        _scrollViewer = scroll;

        async void NotifyScrollViewerChange()
        {
            var verticalOffset = scroll.VerticalOffset;
            var viewport = scroll.ViewportHeight;
            var extent = scroll.ExtentHeight;

            await _viewModel.NotifyScrollPositionAsync(verticalOffset, viewport, extent);
        }

        scroll.RegisterPropertyChangedCallback(ScrollViewer.VerticalOffsetProperty, (s, dp) =>
        {
            NotifyScrollViewerChange();
        });

        scroll.RegisterPropertyChangedCallback(ScrollViewer.ViewportHeightProperty, (s, dp) =>
        {
            NotifyScrollViewerChange();
        });

        scroll.RegisterPropertyChangedCallback(ScrollViewer.ExtentHeightProperty, (s, dp) =>
        {
            NotifyScrollViewerChange();
        });
    }

    public bool GetScrollViewMetrics(out double offsetY, out double viewportHeight, out double extentHeight)
    {
        offsetY = 0; viewportHeight = 0; extentHeight = 0;

        if (_scrollViewer != null)
        {
            offsetY = _scrollViewer.VerticalOffset;
            viewportHeight = _scrollViewer.ViewportHeight;
            extentHeight = _scrollViewer.ExtentHeight;
            return true;
        }
        return false;
    }

    private static T? FindDescendant<T>(DependencyObject start) where T : DependencyObject
    {
        if (start == null) return null;

        var queue = new Queue<DependencyObject>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T t)
                    return t;
                queue.Enqueue(child);
            }
        }

        return null;
    }

    private void UpdateSelectorBarSelection()
    {
        if ((int)_viewModel.SelectedFilter < _FilterSelectorBar.Items.Count)
        {
            _FilterSelectorBar.SelectedItem = _FilterSelectorBar.Items[(int)_viewModel.SelectedFilter];
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs _)
    {
        if (sender is TextBox textBox)
        {
            _viewModel.SearchText = textBox.Text;
        }
    }

    private void FilterSelectorBar_PointerPressed(object _, PointerRoutedEventArgs e)
    {
        // 事件继续传递会导致搜索框失去焦点
        e.Handled = true;
    }

    /// <summary>
    /// Workaround for WinUI3 ScrollView layout cycle bug (https://github.com/microsoft/microsoft-ui-xaml/issues/11040).
    /// 在 150% 等非整除 DPI 下，SelectorBar 内部 ScrollView 的 PART_ScrollBarsSeparator 可见性在
    /// Arrange 阶段反复切换（DesiredSize 在 34.666668px 和 40px 间振荡），耗尽 8 次布局迭代预算后
    /// 崩溃并抛出 AG_E_LAYOUT_CYCLE。禁用竖向滚动条可消除分隔符的触发条件，振荡消除。
    /// </summary>
    private void DisableSelectorBarScrollBars()
    {
        var scrollView = FindDescendant<ScrollView>(_FilterSelectorBar);
        if (scrollView != null)
        {
            scrollView.VerticalScrollBarVisibility = ScrollingScrollBarVisibility.Hidden;
        }
    }

    private void SetNonClientPointerSource()
    {
        RectInt32[] rectArray = [
            GetElementRect(_FilterSelectorBar),
            GetElementRect(_ButtonArea)
        ];

        InputNonClientPointerSource nonClientInputSrc = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        nonClientInputSrc.SetRegionRects(NonClientRegionKind.Passthrough, rectArray);
    }

    private RectInt32 GetElementRect(FrameworkElement element)
    {
        var scale = WindowExtention.GetScaleFactorForPoint(
            this.AppWindow.Position.X + (this.AppWindow.Size.Width / 2),
            this.AppWindow.Position.Y + (this.AppWindow.Size.Height / 2));
        var transform = element.TransformToVisual(null);
        var bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        RectInt32 rect = GetRect(bounds, scale);
        return rect;
    }

    private RectInt32 GetRect(Rect bounds, double scale)
    {
        return new RectInt32(
            _X: (int)Math.Round(bounds.X * scale),
            _Y: (int)Math.Round(bounds.Y * scale),
            _Width: (int)Math.Round(bounds.Width * scale),
            _Height: (int)Math.Round(bounds.Height * scale)
        );
    }

    private void CtrlHome_Invoked(KeyboardAccelerator _, KeyboardAcceleratorInvokedEventArgs args)
    {
        _viewModel.HandleKeyPress(Key.Home, false, false, true);
        args.Handled = true;
    }

    private void CtrlEnd_Invoked(KeyboardAccelerator _, KeyboardAcceleratorInvokedEventArgs args)
    {
        _viewModel.HandleKeyPress(Key.End, false, false, true);
        args.Handled = true;
    }

    public void SetTopmost(bool topmost)
    {
        this.SetIsAlwaysOnTop(topmost);
    }

    public bool SetNearCaretPosition(ScreenPosition caretPosition)
    {
        var displayArea = DisplayArea.GetFromPoint(new PointInt32(caretPosition.X, caretPosition.Y), DisplayAreaFallback.Primary);
        if (displayArea == null)
        {
            return false;
        }

        var workArea = displayArea.WorkArea;
        var (windowWidth, windowHeight) = WindowExtention.DipToPhysical(_viewModel.Width, _viewModel.Height, caretPosition.X, caretPosition.Y);

        var (posX, posY) = WindowPositionHelper.CalculateNearCaretPosition(
            caretPosition, windowWidth, windowHeight,
            workArea.X, workArea.Y, workArea.Width, workArea.Height);

        this.AppWindow.Move(new PointInt32(posX, posY));
        return true;
    }

    public bool SetNearMousePosition(ScreenPosition mousePosition)
    {
        var displayArea = DisplayArea.GetFromPoint(new PointInt32(mousePosition.X, mousePosition.Y), DisplayAreaFallback.Primary);
        if (displayArea == null)
        {
            return false;
        }

        var workArea = displayArea.WorkArea;
        var (windowWidth, windowHeight) = WindowExtention.DipToPhysical(_viewModel.Width, _viewModel.Height, mousePosition.X, mousePosition.Y);

        var (posX, posY) = WindowPositionHelper.CalculateNearMousePosition(
            mousePosition, windowWidth, windowHeight,
            workArea.X, workArea.Y, workArea.Width, workArea.Height);

        this.AppWindow.Move(new PointInt32(posX, posY));
        return true;
    }

    public bool SetPositionOnScreen(int screenX, int screenY)
    {
        var targetDisplayArea = DisplayArea.GetFromPoint(new PointInt32(screenX, screenY), DisplayAreaFallback.Primary);
        if (targetDisplayArea == null)
        {
            return false;
        }

        if (_windowLoaded)
        {
            var currentCenterX = this.AppWindow.Position.X + (this.AppWindow.Size.Width / 2);
            var currentCenterY = this.AppWindow.Position.Y + (this.AppWindow.Size.Height / 2);
            var currentDisplayArea = DisplayArea.GetFromPoint(new PointInt32(currentCenterX, currentCenterY), DisplayAreaFallback.Primary);
            if (currentDisplayArea != null && currentDisplayArea.DisplayId.Value == targetDisplayArea.DisplayId.Value)
            {
                return true;
            }
        }

        var workArea = targetDisplayArea.WorkArea;
        var (windowWidth, windowHeight) = WindowExtention.DipToPhysical(_viewModel.Width, _viewModel.Height, screenX, screenY);

        var (x, y) = WindowPositionHelper.CalculateCenterOnScreenPosition(
            windowWidth, windowHeight,
            workArea.X, workArea.Y, workArea.Width, workArea.Height);

        this.AppWindow.Move(new PointInt32(x, y));
        this.AppWindow.Resize(new SizeInt32(windowWidth, windowHeight));
        return true;
    }

    private void SetWindowMinSize()
    {
        var infiniteSize = new Size(double.PositiveInfinity, double.PositiveInfinity);
        _FilterSelectorBar.Measure(infiniteSize);
        _ButtonArea.Measure(infiniteSize);
        _SearchTextBox.Measure(infiniteSize);

        _windowManger.MinWidth = _FilterSelectorBar.DesiredSize.Width + (_ButtonArea.DesiredSize.Width * 2);
        _windowManger.MinHeight = _FilterSelectorBar.DesiredSize.Height + _SearchTextBox.DesiredSize.Height + 20;
    }

    private static readonly BoolToVisibilityConverter boolToVisibilityConverter = new();
    private static readonly BoolToVisibilityNegateConverter boolToVisibilityNegateConverter = new();

    private void StatusBorderLoaded(object sender, RoutedEventArgs _)
    {
        if (sender is not Border border) return;

        // 直接绑定 ShowSyncStateIndicator（ViewModel 已组合两个条件）
        var binding = new Binding
        {
            Source = _viewModel,
            Path = new PropertyPath(nameof(HistoryViewModel.ShowSyncStateIndicator)),
            Converter = boolToVisibilityConverter,
            Mode = BindingMode.OneWay
        };
        border.SetBinding(UIElement.VisibilityProperty, binding);
    }

    private void RecordControlBarLoaded(object sender, RoutedEventArgs _)
    {
        if (sender is not RecordControlBar controlBar) return;

        // 设置 ViewModel 绑定
        controlBar.ViewModel = _viewModel;

        // 设置 Visibility 绑定（当 ShowPreviewPanel 为 true 时隐藏）
        var visibilityBinding = new Binding
        {
            Source = _viewModel,
            Path = new PropertyPath(nameof(HistoryViewModel.ShowPreviewPanel)),
            Converter = boolToVisibilityNegateConverter,
            Mode = BindingMode.OneWay,
        };
        controlBar.SetBinding(UIElement.VisibilityProperty, visibilityBinding);
    }

    private bool _isDraggingSplitter = false;
    private double _splitterStartX = 0;
    private int _splitterStartWidth = 0;

    private void PreviewSplitter_PointerEntered(object sender, PointerRoutedEventArgs _)
    {
        if (sender is Border border)
        {
            border.Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemControlForegroundAccentBrush"];
        }
    }

    private void PreviewSplitter_PointerExited(object sender, PointerRoutedEventArgs _)
    {
        if (sender is Border border && !_isDraggingSplitter)
        {
            border.Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemControlForegroundBaseLowBrush"];
        }
    }

    private void PreviewSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            _isDraggingSplitter = true;
            _splitterStartX = e.GetCurrentPoint(_MainContentGrid).Position.X;
            _splitterStartWidth = _viewModel.ListViewWidth;
            border.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void PreviewSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSplitter)
            return;

        var currentX = e.GetCurrentPoint(_MainContentGrid).Position.X;
        var delta = currentX - _splitterStartX;
        var newWidth = _splitterStartWidth + (int)delta;

        // 计算最大宽度：确保预览面板至少300像素
        var maxWidth = (int)_MainContentGrid.ActualWidth - 300 - 2;
        if (maxWidth < 150) maxWidth = 150;

        // 限制宽度范围
        newWidth = Math.Clamp(newWidth, 150, maxWidth);

        _viewModel.ListViewWidth = newWidth;
        _ListView.Width = newWidth;
        e.Handled = true;
    }

    private void PreviewSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            _isDraggingSplitter = false;
            border.ReleasePointerCapture(e.Pointer);
            border.Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SystemControlForegroundBaseLowBrush"];
            e.Handled = true;
        }
    }
}
