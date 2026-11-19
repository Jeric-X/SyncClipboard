using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using System;
using SyncClipboard.Core.I18n;

namespace SyncClipboard.Desktop.Views;

public sealed class HistoryTitleDialog : FAContentDialog
{
    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    private readonly TextBox _input;
    public string Text => _input.Text ?? string.Empty;

    public HistoryTitleDialog(string title, string? currentTitle)
    {
        Title = title;
        PrimaryButtonText = Strings.Confirm;
        CloseButtonText = Strings.Cancel;
        DefaultButton = FAContentDialogButton.Primary;
        _input = new TextBox { Text = currentTitle ?? string.Empty };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        Content = new StackPanel { MaxWidth = 340, Spacing = 12, Children = { _input, message } };
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _input.Focus();
            _input.SelectAll();
        });
        PrimaryButtonClick += (_, args) =>
        {
            try
            {
                HistoryTitles.Normalize(Text);
            }
            catch (HistoryTitleValidationException ex)
            {
                args.Cancel = true;
                message.Text = ex.Error switch
                {
                    HistoryTitleError.TooLong => string.Format(Strings.HistoryTitleTooLong, HistoryTitles.MaxLength),
                    HistoryTitleError.LineBreak => Strings.HistoryTitleLineBreak,
                    HistoryTitleError.ControlCharacter => Strings.HistoryTitleControlCharacter,
                    _ => Strings.HistoryTitleInvalidCharacter
                };
                message.IsVisible = true;
                _input.Focus();
            }
        };
    }
}
