using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.WinUI3.Win32;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using WinUIEx;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class HistoryWindow : Window, IWindow
{
    private readonly HistoryViewModel _viewModel;
    public HistoryViewModel ViewModel => _viewModel;
    private bool _windowLoaded = false;
    private readonly MultiTimesEventSimulator _historyItemEvents = new(TimeSpan.FromMilliseconds(300));
    private readonly MultiTimesEventSimulator _imageClickEvents = new(TimeSpan.FromMilliseconds(300));

    public HistoryWindow(ConfigManager configManager, HistoryViewModel viewModel)
    {
        _viewModel = viewModel;
        this.AppWindow.Resize(new SizeInt32(1200, 800));

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_DraggableArea);
        this.SetTitleBarButtonForegroundColor();

        this.AppWindow.Resize(new SizeInt32(_viewModel.Width, _viewModel.Height));
        this.SizeChanged += HistoryWindow_SizeChanged;

        configManager.GetAndListenConfig<ProgramConfig>(config => this.SetTheme(config.Theme));

        this.Closed += OnHistoryWindowClosed;

        this.Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                if (configManager.GetConfig<HistoryConfig>().CloseWhenLostFocus)
                {
                    this.Hide();
                }
            }
        };

        _imageClickEvents[2] += () =>
        {
            if (_ListView.SelectedValue is not HistoryRecordVM record)
            {
                return;
            }
            _viewModel.ViewImageCommand.Execute(record);
        };

        _historyItemEvents[2] += async () =>
        {
            if (_ListView.SelectedValue is not HistoryRecordVM record)
            {
                return;
            }
            this.Hide();
            await _viewModel.CopyToClipboard(record, false, CancellationToken.None);
        };
    }

    private void HistoryWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        if (_windowLoaded)
        {
            _viewModel.Height = this.AppWindow.Size.Height;
            _viewModel.Width = this.AppWindow.Size.Width;
        }
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
            this.CenterOnScreen();
            _ = _viewModel.Init();
        }

        this.Activate();
        this.SetForegroundWindow();
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
            this.Hide();
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
            this.Hide();
        }
    }

    private void Grid_KeyDown(object _, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            this.Hide();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Down || e.Key == VirtualKey.Up)
        {
            KeyUpDownPressed(e.Key);
            return;
        }
    }

    private void KeyUpDownPressed(VirtualKey key)
    {
        if (_ListView.Items.Count == 0)
        {
            return;
        }

        if (key == VirtualKey.Down)
        {
            if (_ListView.Items.Count > (_ListView.SelectedIndex + 1))
            {
                _ListView.SelectedIndex++;
            }
        }
        else if (key == VirtualKey.Up)
        {
            if (_ListView.SelectedIndex > 0)
            {
                _ListView.SelectedIndex--;
            }
        }
        _ListView.ScrollIntoView(_ListView.SelectedItem);
    }

    private async void EnterKeyTriggered(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_ListView.SelectedValue is not HistoryRecordVM record)
        {
            return;
        }
        args.Handled = true;
        this.Hide();
        var paste = sender.Modifiers != VirtualKeyModifiers.Menu;
        await _viewModel.CopyToClipboard(record, paste, CancellationToken.None);
    }

    private void Image_ImageOpened(object sender, RoutedEventArgs _)
    {
        if (sender is not Image image)
        {
            return;
        }

        _InvisualableImage.Source = image.Source;
        _InvisualableImage.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desiredSize = _InvisualableImage.DesiredSize;

        if (desiredSize.Height > 200)
        {
            image.Stretch = Stretch.Uniform;
        }
        image.Visibility = Visibility.Visible;
        _InvisualableImage.Source = null;
    }

    private void Grid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        SetSelectedItem((HistoryRecordVM?)((Grid?)sender)?.DataContext);
        _historyItemEvents.TriggerOriginalEvent();
    }

    private void Image_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        SetSelectedItem((HistoryRecordVM?)((Image?)sender)?.DataContext);
        _imageClickEvents.TriggerOriginalEvent();
    }

    private void SetSelectedItem(HistoryRecordVM? record)
    {
        if (record == null)
        {
            return;
        }
        _ListView.SelectedValue = record;
    }
}
