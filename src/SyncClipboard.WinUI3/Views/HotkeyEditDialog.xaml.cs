using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.Models.Keyboard;
using System.Windows.Input;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HotkeyEditDialog : ContentDialog
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(Hotkey), typeof(HotkeyEditDialog), new PropertyMetadata(Hotkey.Nothing));

    public static readonly DependencyProperty IsErrorProperty = DependencyProperty.Register(
        nameof(IsError), typeof(bool), typeof(HotkeyEditDialog), new PropertyMetadata(false));

    public static readonly DependencyProperty ErrorMessageProperty = DependencyProperty.Register(
        nameof(ErrorMessage), typeof(string), typeof(HotkeyEditDialog), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SaveCommandProperty = DependencyProperty.Register(
        nameof(SaveCommand), typeof(ICommand), typeof(HotkeyEditDialog), new PropertyMetadata(null));

    public Hotkey Hotkey
    {
        get => (Hotkey)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public bool IsError
    {
        get => (bool)GetValue(IsErrorProperty);
        set => SetValue(IsErrorProperty, value);
    }

    public string ErrorMessage
    {
        get => (string)GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public ICommand? SaveCommand
    {
        get => (ICommand?)GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
    }

    public HotkeyEditDialog() => InitializeComponent();

    private void DialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args) =>
        _Input.Focus(FocusState.Programmatic);

    private void ClearClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Hotkey = Hotkey.Nothing;
        _Input.Focus(FocusState.Programmatic);
        args.Cancel = true;
    }

    private void ConfirmClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (SaveCommand?.CanExecute(null) != true)
        {
            args.Cancel = true;
            return;
        }
        SaveCommand.Execute(null);
        args.Cancel = IsError;
    }
}
