using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.Models.Keyboard;
using System;
using System.Windows.Input;

namespace SyncClipboard.Desktop.Views;

public partial class HotkeyEditDialog : FAContentDialog
{
    private Control? _layoutRoot;

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    public static readonly StyledProperty<Hotkey> HotkeyProperty =
        AvaloniaProperty.Register<HotkeyEditDialog, Hotkey>(nameof(Hotkey), Hotkey.Nothing);

    public static readonly StyledProperty<bool> IsErrorProperty =
        AvaloniaProperty.Register<HotkeyEditDialog, bool>(nameof(IsError));

    public static readonly StyledProperty<string> ErrorMessageProperty =
        AvaloniaProperty.Register<HotkeyEditDialog, string>(nameof(ErrorMessage), string.Empty);

    public static readonly StyledProperty<ICommand?> SaveCommandProperty =
        AvaloniaProperty.Register<HotkeyEditDialog, ICommand?>(nameof(SaveCommand));

    public Hotkey Hotkey
    {
        get => GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public bool IsError
    {
        get => GetValue(IsErrorProperty);
        set => SetValue(IsErrorProperty, value);
    }

    public string ErrorMessage
    {
        get => GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public ICommand? SaveCommand
    {
        get => GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
    }

    public HotkeyEditDialog() => InitializeComponent();

    protected override void OnOpening()
    {
        // FluentAvalonia disables hit testing on close but does not restore it when reusing a dialog.
        SetCurrentValue(IsHitTestVisibleProperty, true);
        base.OnOpening();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _layoutRoot = e.NameScope.Find<Control>("LayoutRoot");
    }

    private void DialogOpened(FAContentDialog _, EventArgs _1)
    {
        // The closing animation leaves a local IsVisible=false on LayoutRoot. Reset it so that
        // completing the next opening animation cannot briefly hide the content and clear focus.
        _layoutRoot?.SetValue(IsVisibleProperty, true);
        _Input.Focus();
    }

    private void ClearClick(FAContentDialog _, FAContentDialogButtonClickEventArgs args)
    {
        SetCurrentValue(HotkeyProperty, Hotkey.Nothing);
        _Input.Focus();
        args.Cancel = true;
    }

    private void ConfirmClick(FAContentDialog _, FAContentDialogButtonClickEventArgs args)
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
