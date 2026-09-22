using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.Keyboard;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class HistorySettingPage : UserControl
{
    private readonly HistorySettingViewModel _viewModel;
    public HistorySettingPage()
    {
        InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<HistorySettingViewModel>();
        DataContext = _viewModel;
    }

    private async void EditShortcutClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HistoryShortcutSetting setting })
            return;
        _viewModel.BeginEditShortcut(setting);
        var input = new HotkeyInput
        {
            MinWidth = 360,
            Height = 60,
            FontSize = 20,
            [!HotkeyInput.HotkeyProperty] = ViewModelBinding(nameof(_viewModel.EditingShortcut), BindingMode.TwoWay),
            [!HotkeyInput.IsErrorProperty] = ViewModelBinding(nameof(_viewModel.ShortcutHasError))
        };
        var dialog = new FAContentDialog
        {
            Title = setting.Name,
            PrimaryButtonText = Strings.Confirm,
            SecondaryButtonText = Strings.Clear,
            CloseButtonText = Strings.Cancel,
            [!FAContentDialog.IsPrimaryButtonEnabledProperty] = ViewModelBinding(nameof(_viewModel.CanSaveShortcut)),
            [!FAContentDialog.PrimaryButtonCommandProperty] = ViewModelBinding(nameof(_viewModel.SaveShortcutCommand)),
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    input,
                    new TextBlock
                    {
                        Text = Strings.HistoryShortcutInvalid,
                        MaxWidth = 420,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        [!IsVisibleProperty] = ViewModelBinding(nameof(_viewModel.ShortcutHasError))
                    }
                }
            }
        };
        dialog.Opened += (_, _) => input.Focus();
        dialog.SecondaryButtonClick += (_, args) =>
        {
            _viewModel.EditingShortcut = Hotkey.Nothing;
            input.Focus();
            args.Cancel = true;
        };
        await dialog.ShowAsync(App.Current.MainWindow);
    }

    private Binding ViewModelBinding(string path, BindingMode mode = BindingMode.Default) =>
        new(path) { Source = _viewModel, Mode = mode };
}
