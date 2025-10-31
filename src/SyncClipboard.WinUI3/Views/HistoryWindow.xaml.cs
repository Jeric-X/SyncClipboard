using CommunityToolkit.WinUI.Converters;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.WinUI3.Win32;
using System;
using System.Collections.Generic;
using System.Threading;
using Windows.Foundation;
using Windows.Graphics;
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

    public HistoryWindow(ConfigManager configManager, HistoryViewModel viewModel)
    {
        _viewModel = viewModel;
        _windowManger = WindowManager.Get(this);
        this.AppWindow.Resize(new SizeInt32(1200, 800));

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_TitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        this.SetTitleBarButtonForegroundColor();
        _TitleBar.Loaded += (_, _) => SetNonClientPointerSource();

        this.AppWindow.Resize(new SizeInt32(_viewModel.Width, _viewModel.Height));
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
    }

    private void HistoryWindow_SizeChanged(object sender, Microsoft.UI.Xaml.WindowSizeChangedEventArgs args)
    {
        if (_windowLoaded)
        {
            _viewModel.Height = this.AppWindow.Size.Height;
            _viewModel.Width = this.AppWindow.Size.Width;
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
            this.CenterOnScreen();
            _ = _viewModel.Init(this);
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
        this.SetForegroundWindow();
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
            throw new NotSupportedException($"WinUI3 VirtualKey '{e.Key}' is not supported by KeyboardMap. Please add mapping for this key.");
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
        image.Visibility = Visibility.Visible;
    }

    private void Grid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        var clickedItem = (HistoryRecordVM)((Grid?)sender)?.DataContext!;
        if ((HistoryRecordVM?)_ListView.SelectedValue != clickedItem)
        {
            _ListView.SelectedValue = clickedItem;
            _historyItemEvents.Reset();
        }
        _historyItemEvents.TriggerOriginalEvent();
    }

    private void Image_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
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
        var scale = _TitleBar.XamlRoot.RasterizationScale;
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

    private void SetWindowMinSize()
    {
        var infiniteSize = new Size(double.PositiveInfinity, double.PositiveInfinity);
        _FilterSelectorBar.Measure(infiniteSize);
        _ButtonArea.Measure(infiniteSize);
        _SearchTextBox.Measure(infiniteSize);

        _windowManger.MinWidth = _FilterSelectorBar.DesiredSize.Width + _ButtonArea.DesiredSize.Width * 2;
        _windowManger.MinHeight = _FilterSelectorBar.DesiredSize.Height + _SearchTextBox.DesiredSize.Height + 20;
    }

    private static readonly BoolToVisibilityConverter boolToVisibilityConverter = new();
    private void StatusBorderLoaded(object sender, RoutedEventArgs _)
    {
        var border = sender as Border;
        if (border == null) return;

        var visualbilityBinding = new Binding
        {
            Source = _viewModel,
            Path = new PropertyPath(nameof(HistoryViewModel.ShowSyncState)),
            Converter = boolToVisibilityConverter,
            Mode = BindingMode.OneWay
        };

        border.SetBinding(UIElement.VisibilityProperty, visualbilityBinding);
    }
}
