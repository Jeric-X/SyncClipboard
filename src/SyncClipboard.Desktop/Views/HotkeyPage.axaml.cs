using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.ViewModels;
using System;

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
        ViewModel.EditingGuid = (Guid)((Button)sender!).DataContext!;
        var dialog = new ContentDialog
        {
            [!ContentDialog.IsPrimaryButtonEnabledProperty] = ViewModelBinding("SetHotkeyCanExecute"),
            [!ContentDialog.PrimaryButtonCommandProperty] = ViewModelBinding("SetHotkeyCommand"),
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
        await dialog.ShowAsync();
    }

    private Binding ViewModelBinding(string path, BindingMode mode = BindingMode.Default)
    {
        return new Binding(path, mode) { Source = ViewModel };
    }

    private void ClearButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ViewModel.EditingHotkey = Hotkey.Nothing;
        (sender.Content as UserControl)?.Focus();
        args.Cancel = true;
    }

    private void SettingsExpander_Loaded(object? sender, RoutedEventArgs _)
    {
        if (sender is SettingsExpander settingsExpander)
        {
            settingsExpander.IsExpanded = true;
        }
    }
}
