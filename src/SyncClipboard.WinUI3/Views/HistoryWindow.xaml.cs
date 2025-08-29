using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.WinUI3.Win32;
using System.Threading;
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

    private async void PasteButtonClicked(object sender, RoutedEventArgs e)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is HistoryRecord record)
        {
            this.Hide();
            await _viewModel.CopyToClipboard(record, true, CancellationToken.None);
        }
    }
    private void ListViewItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var history = ((ListViewItem?)sender)?.DataContext;
        if (history is HistoryRecord record)
        {
            _ = _viewModel.CopyToClipboard(record, false, CancellationToken.None);
            this.Hide();
        }
    }

    private void DeleteButtonClicked(object sender, RoutedEventArgs e)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is HistoryRecord record)
        {
            _viewModel.DeleteItem(record);
        }
    }

    private void CopyButtonClicked(object sender, RoutedEventArgs e)
    {
        var history = ((Button?)sender)?.DataContext;
        if (history is HistoryRecord record)
        {
            _ = _viewModel.CopyToClipboard(record, false, CancellationToken.None);
            this.Hide();
        }
    }

    private void Grid_KeyDown(object _sender, KeyRoutedEventArgs e)
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
        if (_ListView.SelectedValue is not HistoryRecord record)
        {
            return;
        }
        args.Handled = true;
        this.Hide();
        var paste = sender.Modifiers != VirtualKeyModifiers.Menu;
        await _viewModel.CopyToClipboard(record, paste, CancellationToken.None);
    }

    private void Image_ImageOpened(object sender, RoutedEventArgs e)
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
}
