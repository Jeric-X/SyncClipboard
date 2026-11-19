using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using SyncClipboard.Core.I18n;

namespace SyncClipboard.WinUI3.Views;

public sealed class HistoryTitleDialog : ContentDialog
{
    private readonly TextBox _input;
    public string Text => _input.Text ?? string.Empty;

    public HistoryTitleDialog(string title, string? currentTitle)
    {
        Title = title;
        PrimaryButtonText = Strings.Confirm;
        CloseButtonText = Strings.Cancel;
        DefaultButton = ContentDialogButton.Primary;
        _input = new TextBox { Text = currentTitle ?? string.Empty };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        Content = new StackPanel { MaxWidth = 340, Spacing = 12, Children = { _input, message } };
        Opened += (_, _) =>
        {
            _input.Focus(FocusState.Programmatic);
            _input.SelectAll();
        };
        PrimaryButtonClick += (_, args) =>
        {
            try
            {
                HistoryTitles.Normalize(Text);
            }
            catch (ArgumentException)
            {
                args.Cancel = true;
                message.Text = string.Format(Strings.HistoryTitleFormatError, HistoryTitles.MaxLength);
                message.Visibility = Visibility.Visible;
                _input.Focus(FocusState.Programmatic);
            }
        };
    }
}
