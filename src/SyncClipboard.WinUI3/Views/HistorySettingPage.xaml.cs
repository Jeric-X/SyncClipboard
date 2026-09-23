using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.Keyboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HistorySettingPage : Page
{
    private readonly HistorySettingViewModel _viewModel;
    public HistorySettingPage()
    {
        InitializeComponent();
        _viewModel = App.Current.Services.GetRequiredService<HistorySettingViewModel>();
    }

    private async void EditShortcutClick(object sender, RoutedEventArgs _)
    {
        if (sender is not Button { DataContext: HistoryShortcutSetting setting })
            return;
        _viewModel.BeginEditShortcut(setting);
        var input = new HotkeyInput { MinWidth = 360, Height = 60, FontSize = 20 };
        input.SetBinding(HotkeyInput.HotkeyProperty, ViewModelBinding(nameof(_viewModel.EditingShortcut), BindingMode.TwoWay));
        var error = new TextBlock
        {
            MaxWidth = 420,
            TextWrapping = TextWrapping.Wrap
        };
        error.SetBinding(TextBlock.TextProperty, ViewModelBinding(nameof(_viewModel.ShortcutErrorMessage)));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = setting.Name,
            PrimaryButtonText = Strings.Confirm,
            SecondaryButtonText = Strings.Clear,
            CloseButtonText = Strings.Cancel,
            Content = new StackPanel { Spacing = 10, Children = { input, error } }
        };
        dialog.SetBinding(ContentDialog.IsPrimaryButtonEnabledProperty, ViewModelBinding(nameof(_viewModel.CanSaveShortcut)));
        void UpdateError(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(_viewModel.ShortcutHasError))
            {
                input.IsError = _viewModel.ShortcutHasError;
                error.Visibility = _viewModel.ShortcutHasError ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        error.Visibility = _viewModel.ShortcutHasError ? Visibility.Visible : Visibility.Collapsed;
        _viewModel.PropertyChanged += UpdateError;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            _viewModel.SaveShortcutCommand.Execute(null);
            args.Cancel = _viewModel.ShortcutHasError;
        };
        dialog.Opened += (_, _) => input.Focus(FocusState.Programmatic);
        dialog.SecondaryButtonClick += (_, args) =>
        {
            _viewModel.EditingShortcut = Hotkey.Nothing;
            input.Focus(FocusState.Programmatic);
            args.Cancel = true;
        };
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            _viewModel.PropertyChanged -= UpdateError;
        }
    }

    private Binding ViewModelBinding(string path, BindingMode mode = BindingMode.OneWay) =>
        new() { Path = new PropertyPath(path), Source = _viewModel, Mode = mode };

    private async void ResetShortcutClick(object sender, RoutedEventArgs _)
    {
        if (sender is Button { DataContext: HistoryShortcutSetting setting })
            await _viewModel.ResetShortcutCommand.ExecuteAsync(setting);
    }
}
