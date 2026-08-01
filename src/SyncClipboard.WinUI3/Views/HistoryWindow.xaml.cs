using CommunityToolkit.WinUI.Converters;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
using SyncClipboard.WinUI3.Utilities;
using SyncClipboard.WinUI3.ValueConverters;
using SyncClipboard.WinUI3.Win32;
using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.System;
using Windows.UI.Core;
using WinUIEx;
using XamlWindowSizeChangedEventArgs = Microsoft.UI.Xaml.WindowSizeChangedEventArgs;

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
    private HistoryRecordVM? _lastHistoryClickRecord;
    private HistoryRecordVM? _lastImageClickRecord;
    private readonly WindowManager _windowManger;
    private ScrollViewer? _scrollViewer = null;
    private readonly ICaretPositionProvider _caretPositionProvider;
    private readonly ILogger _logger;
    private readonly PointerEventHandler _listViewItemPointerPressedHandler;
    private readonly PointerEventHandler _listViewItemPointerReleasedHandler;
    private readonly PointerEventHandler _listViewItemPointerExitedHandler;
    private readonly TypedEventHandler<UIElement, ContextRequestedEventArgs> _listViewItemContextRequestedHandler;

    public HistoryWindow(ConfigManager configManager, HistoryViewModel viewModel, ICaretPositionProvider caretPositionProvider, ILogger logger)
    {
        _viewModel = viewModel;
        _windowManger = WindowManager.Get(this);
        _caretPositionProvider = caretPositionProvider;
        _logger = logger;
        _listViewItemPointerPressedHandler = ListViewItem_PointerPressed;
        _listViewItemPointerReleasedHandler = ListViewItem_PointerReleased;
        _listViewItemPointerExitedHandler = ListViewItem_PointerExited;
        _listViewItemContextRequestedHandler = ListViewItem_ContextRequested;
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
                ResetPointerInteractionState();
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
            if (_lastImageClickRecord is { } record)
                _viewModel.HandleImageDoubleClick(record);
        };

        _historyItemEvents[2] += () =>
        {
            if (_lastHistoryClickRecord is { } record)
                _viewModel.HandleItemDoubleClick(record);
        };

        // 初始化 SelectorBar 选项
        InitializeSelectorBar();

        _ListView.SizeChanged += OnListViewSizeChanged;

        InitializeScrollWatcher();

        this.SetTopmost(_viewModel.IsTopmost);

        ApplyFontScale(_viewModel.FontScalePercent);
        ApplyCompactListMode(_viewModel.IsCompactListMode);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ((INotifyCollectionChanged)_viewModel.VisibleSelectedItems).CollectionChanged += OnVisibleSelectedItemsCollectionChanged;
        ApplyListViewSelectionMode();
        UpdateSelectAllIcon();
        UpdateToggleStarIcon();
        UpdateListViewWidthForPreview(); // 初始化时设置ListView宽度
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HistoryViewModel.FontScalePercent):
                ApplyFontScale(_viewModel.FontScalePercent);
                break;
            case nameof(HistoryViewModel.ShowPreviewPanel):
                UpdateListViewWidthForPreview();
                break;
            case nameof(HistoryViewModel.ListViewWidth):
                ApplyListViewWidth();
                break;
            case nameof(HistoryViewModel.IsCompactListMode):
                ApplyCompactListMode(_viewModel.IsCompactListMode);
                break;
            case nameof(HistoryViewModel.FilterOptions):
                InitializeSelectorBar();
                break;
            case nameof(HistoryViewModel.SelectedFilter):
                UpdateSelectorBarSelection();
                break;
            case nameof(HistoryViewModel.IsMultiSelecting):
                ApplyListViewSelectionMode();
                break;
            case nameof(HistoryViewModel.SelectedIndex):
                ApplySelectedIndex();
                break;
            case nameof(HistoryViewModel.IsCurrentFilterFullySelected):
                UpdateSelectAllIcon();
                break;
            case nameof(HistoryViewModel.AreSelectedRecordsStarred):
                UpdateToggleStarIcon();
                break;
            default:
                break;
        }
    }

    private void ApplyListViewWidth()
    {
        if (_viewModel.ShowPreviewPanel)
            _ListView.Width = _viewModel.ListViewWidth;
    }

    private void ApplyListViewSelectionMode()
    {
        HistoryListModeProxy.Current.IsMultiSelecting = _viewModel.IsMultiSelecting;
        var selectionMode = _viewModel.IsMultiSelecting
            ? ListViewSelectionMode.Multiple
            : ListViewSelectionMode.Single;
        if (_ListView.SelectionMode != selectionMode)
            _ListView.SelectionMode = selectionMode;

        if (selectionMode == ListViewSelectionMode.Multiple)
            ApplyVisibleSelectionSnapshot();
        else
            ApplySelectedIndex();
    }

    private void ApplySelectedIndex()
    {
        if (!_viewModel.IsMultiSelecting)
            _ListView.SelectedIndex = _viewModel.SelectedIndex;
    }

    private void ApplyCompactListMode(bool isCompactListMode)
    {
        HistoryListModeProxy.Current.IsCompactListMode = isCompactListMode;
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

    private void HistoryWindow_SizeChanged(object _, XamlWindowSizeChangedEventArgs __)
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
        ResetPointerInteractionState();
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
            ResetPointerInteractionState();
            this.AppWindow.Hide();
        }
    }

    private void ResetPointerInteractionState()
    {
        _viewModel.CancelMultiSelectLongPress();
    }

    private async void PasteButtonClicked(object sender, RoutedEventArgs _)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is HistoryRecordVM record)
        {
            await _viewModel.HandleCopyButtonAsync(record, true);
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

    private async void CopyButtonClicked(object sender, RoutedEventArgs _)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is HistoryRecordVM record)
        {
            await _viewModel.HandleCopyButtonAsync(record, false);
        }
    }

    private void Grid_KeyDown(object _, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && _viewModel.IsMultiSelecting)
        {
            _viewModel.ExitMultiSelect();
            e.Handled = true;
            return;
        }

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

        _InvisualableImage.Source = image.Source;
        _InvisualableImage.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _InvisualableImage.Source = null;
        var desiredSize = _InvisualableImage.DesiredSize;

        if (desiredSize.Height > 200)
        {
            image.Stretch = Stretch.Uniform;
        }

        BindLoadedImageVisibility(image);
    }

    private static void BindLoadedImageVisibility(Image image)
    {
        var binding = new Binding
        {
            Source = HistoryListModeProxy.Current,
            Path = new PropertyPath(nameof(HistoryListModeProxy.IsCompactListMode)),
            Converter = boolToVisibilityNegateConverter,
            Mode = BindingMode.OneWay,
        };
        image.SetBinding(UIElement.VisibilityProperty, binding);
    }

    private void ListViewItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ListViewItem { Content: HistoryRecordVM record } container
            || e.OriginalSource is not DependencyObject source)
            return;

        var ctrlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var shiftPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        var (isPrimaryPressed, middleButtonClicked, isRightButtonPressed) = GetPointerButtonState(e, container);
        if (!isRightButtonPressed && IsInteractiveItemChild(source, container))
            return;

        e.Handled = _viewModel.HandleItemClick(
            record,
            isPrimaryPressed && ctrlPressed,
            isPrimaryPressed && shiftPressed,
            isPrimaryPressed,
            middleButtonClicked,
            isRightButtonPressed);

        if (!isPrimaryPressed)
            return;

        HandlePrimaryPointerPressed(record, source, container, e.Handled);
    }

    private static (bool IsPrimaryPressed, bool MiddleButtonClicked, bool IsRightButtonPressed) GetPointerButtonState(
        PointerRoutedEventArgs e,
        ListViewItem container)
    {
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
            return (true, false, false);

        var properties = e.GetCurrentPoint(container).Properties;
        return (properties.IsLeftButtonPressed, properties.IsMiddleButtonPressed, properties.IsRightButtonPressed);
    }

    private void HandlePrimaryPointerPressed(
        HistoryRecordVM record,
        DependencyObject source,
        ListViewItem container,
        bool handled)
    {
        var dragSource = FindItemDragSource(source, container);
        if (dragSource is not null)
            dragSource.CanDrag = !handled;

        if (!handled)
            _viewModel.BeginMultiSelectLongPress(record);

        TrackItemClick(record, dragSource is Image);
    }

    private void TrackItemClick(HistoryRecordVM record, bool isImage)
    {
        if (isImage)
        {
            if (!ReferenceEquals(_lastImageClickRecord, record))
            {
                _lastImageClickRecord = record;
                _imageClickEvents.Reset();
            }
            _imageClickEvents.TriggerOriginalEvent();
        }
        else
        {
            if (!ReferenceEquals(_lastHistoryClickRecord, record))
            {
                _lastHistoryClickRecord = record;
                _historyItemEvents.Reset();
            }
            _historyItemEvents.TriggerOriginalEvent();
        }
    }

    private void ListViewItem_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _viewModel.CancelMultiSelectLongPress();
    }

    private void ListViewItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _viewModel.CancelMultiSelectLongPress();
    }

    private static bool IsInteractiveItemChild(DependencyObject source, ListViewItem container)
    {
        for (DependencyObject? current = source;
             current is not null && !ReferenceEquals(current, container);
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is Button or CheckBox)
                return true;
        }

        return false;
    }

    private static FrameworkElement? FindItemDragSource(DependencyObject source, ListViewItem container)
    {
        for (DependencyObject? current = source;
             current is not null && !ReferenceEquals(current, container);
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is Image image)
                return image;
            if (current is Grid { Name: "HistoryItemContent" } grid)
                return grid;
        }

        return null;
    }

    private async void Item_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
        _viewModel.CancelMultiSelectLongPress();
        if (_viewModel.IsMultiSelecting)
        {
            e.Cancel = true;
            return;
        }

        var draggedItem = (HistoryRecordVM)((FrameworkElement)sender).DataContext;
        if (draggedItem == null)
        {
            e.Cancel = true;
            return;
        }

        try
        {
            e.Data.RequestedOperation = DataPackageOperation.Copy;
            await DragUiHelper.SetDragIconAsync(e.DragUI, draggedItem);
            var success = await _viewModel.FillDragPackage(e.Data, draggedItem);

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

    private void UpdateSelectAllIcon() => _SelectAllIcon.Glyph = _viewModel.IsCurrentFilterFullySelected == true
        ? "\uE73A"
        : "\uE739";

    private void UpdateToggleStarIcon() => _ToggleStarIcon.Symbol = _viewModel.AreSelectedRecordsStarred
        ? Symbol.SolidStar
        : Symbol.OutlineStar;

    private void ListView_ContainerContentChanging(ListViewBase _, ContainerContentChangingEventArgs e)
    {
        if (e.ItemContainer is not ListViewItem container)
            return;

        if (e.InRecycleQueue)
        {
            ClearHistoryItemContainer(container);
            return;
        }

        PrepareHistoryItemContainer(container);
        if (_viewModel.IsMultiSelecting && e.Item is HistoryRecordVM record)
            container.IsSelected = record.IsSelected;
    }

    private void PrepareHistoryItemContainer(ListViewItem container)
    {
        ClearHistoryItemContainer(container);
        container.AddHandler(
            UIElement.PointerPressedEvent,
            _listViewItemPointerPressedHandler,
            true);
        container.AddHandler(UIElement.PointerReleasedEvent, _listViewItemPointerReleasedHandler, true);
        container.AddHandler(UIElement.PointerExitedEvent, _listViewItemPointerExitedHandler, true);
        container.AddHandler(UIElement.ContextRequestedEvent, _listViewItemContextRequestedHandler, true);
    }

    private void ClearHistoryItemContainer(ListViewItem container)
    {
        container.RemoveHandler(UIElement.PointerPressedEvent, _listViewItemPointerPressedHandler);
        container.RemoveHandler(UIElement.PointerReleasedEvent, _listViewItemPointerReleasedHandler);
        container.RemoveHandler(UIElement.PointerExitedEvent, _listViewItemPointerExitedHandler);
        container.RemoveHandler(UIElement.ContextRequestedEvent, _listViewItemContextRequestedHandler);
    }

    private void ApplyVisibleSelectionSnapshot()
    {
        if (!_viewModel.IsMultiSelecting)
            return;

        _ListView.SelectedItems.Clear();
        foreach (var record in _viewModel.VisibleSelectedItems)
            SetVisibleRecordSelected(record, true);
    }

    private void SetVisibleRecordSelected(HistoryRecordVM record, bool selected)
    {
        var current = _ListView.SelectedItems.OfType<HistoryRecordVM>()
            .FirstOrDefault(item => ReferenceEquals(item, record));
        if (selected && current is null)
            _ListView.SelectedItems.Add(record);
        else if (!selected && current is not null)
            _ListView.SelectedItems.Remove(current);
    }

    private void OnVisibleSelectedItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var removed = e.OldItems?.OfType<HistoryRecordVM>().ToArray() ?? [];
        var added = e.NewItems?.OfType<HistoryRecordVM>().ToArray() ?? [];

        void ApplyRemovals()
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                _ListView.SelectedItems.Clear();
            else
                foreach (var record in removed)
                    SetVisibleRecordSelected(record, false);
        }

        if (DispatcherQueue.HasThreadAccess)
            ApplyRemovals();
        else
            DispatcherQueue.TryEnqueue(ApplyRemovals);

        if (added.Length > 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_viewModel.IsMultiSelecting)
                    return;
                foreach (var record in added)
                {
                    if (_viewModel.VisibleSelectedItems.Any(item => ReferenceEquals(item, record))
                        && _viewModel.HistoryItems.Any(item => ReferenceEquals(item, record)))
                        SetVisibleRecordSelected(record, true);
                }
            });
        }
    }

    private void RecordSelectionCheckBox_Click(object sender, RoutedEventArgs _)
    {
        if (sender is FrameworkElement { DataContext: HistoryRecordVM record })
            _viewModel.HandleSelectionCheckBoxClick(record);
    }

    public void ScrollToTop()
    {
        _scrollViewer?.ScrollToVerticalOffset(0);
    }

    private async void ListViewItem_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (_viewModel.IsMultiSelecting
            || sender is not ListViewItem { Content: HistoryRecordVM record } container)
            return;

        var hasPosition = e.TryGetPosition(container, out var position);
        _viewModel.SelectSingleRecord(record);
        var flyout = new MenuFlyout();
        var actions = await _viewModel.BuildActionsAsync(record);
        if (_viewModel.IsMultiSelecting || !ReferenceEquals(container.Content, record))
            return;

        foreach (var action in actions)
        {
            var item = new MenuFlyoutItem { Text = action.Text };
            if (action.Action is not null)
            {
                item.Click += (_, __) => action.Action();
            }
            flyout.Items.Add(item);
        }

        if (hasPosition)
            flyout.ShowAt(container, new FlyoutShowOptions { Position = position });
        else
            flyout.ShowAt(container);
    }

    private void SelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs _)
    {
        if (sender.SelectedItem?.DataContext is LocaleString<HistoryFilterType> option)
        {
            _viewModel.SelectedFilter = option.Key;
        }
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

        UpdateSelectorBarSelection();
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
        foreach (var item in _FilterSelectorBar.Items)
        {
            if (item.DataContext is LocaleString<HistoryFilterType> option && option.Key == _viewModel.SelectedFilter)
            {
                _FilterSelectorBar.SelectedItem = item;
                return;
            }
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
            border.Background = (Brush)Application.Current.Resources["SystemControlForegroundAccentBrush"];
        }
    }

    private void PreviewSplitter_PointerExited(object sender, PointerRoutedEventArgs _)
    {
        if (sender is Border border && !_isDraggingSplitter)
        {
            border.Background = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"];
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
            border.Background = (Brush)Application.Current.Resources["SystemControlForegroundBaseLowBrush"];
            e.Handled = true;
        }
    }
}
