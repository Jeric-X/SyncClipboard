using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Desktop.Views;

public partial class HotkeyPage : UserControl
{
    public HotkeyViewModel ViewModel { get; }

    public HotkeyPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<HotkeyViewModel>();
        DataContext = ViewModel;
    }

    private async void EditButtonClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.EditingHotkey = Hotkey.Nothing;
        ViewModel.EditingCmdId = (string)((Button)sender!).DataContext!;
        var dialog = new FAContentDialog
        {
            [!FAContentDialog.IsPrimaryButtonEnabledProperty] = ViewModelBinding("SetHotkeyCanExecute"),
            [!FAContentDialog.PrimaryButtonCommandProperty] = ViewModelBinding("SetHotkeyCommand"),
            SecondaryButtonText = Strings.Clear,
            CloseButtonText = Strings.Cancel,
            PrimaryButtonText = Strings.Confirm,
            Content = new HotkeyInput
            {
                Width = 500,
                Height = 60,
                FontSize = 20,
                [!HotkeyInput.IsErrorProperty] = ViewModelBinding("IsEditingHasError"),
                [!HotkeyInput.HotkeyProperty] = ViewModelBinding("EditingHotkey", BindingMode.TwoWay)
            }
        };
        dialog.SecondaryButtonClick += ClearButtonClick;
        await dialog.ShowAsync(App.Current.MainWindow);
    }

    private Binding ViewModelBinding(string path, BindingMode mode = BindingMode.Default)
    {
        return new Binding(path) { Mode = mode, Source = ViewModel };
    }

    private void ClearButtonClick(FAContentDialog sender, FAContentDialogButtonClickEventArgs args)
    {
        ViewModel.EditingHotkey = Hotkey.Nothing;
        (sender.Content as UserControl)?.Focus();
        args.Cancel = true;
    }

    private void SettingsExpander_Loaded(object? sender, RoutedEventArgs _)
    {
        if (sender is FASettingsExpander settingsExpander)
        {
            settingsExpander.IsExpanded = true;
        }
    }
}
