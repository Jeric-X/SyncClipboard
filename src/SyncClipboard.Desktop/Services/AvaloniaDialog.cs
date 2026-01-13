using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Services;

public class AvaloniaDialog : IMainWindowDialog
{
    private readonly Window? _window;

    public AvaloniaDialog()
    {
    }

    public AvaloniaDialog(Window window)
    {
        _window = window;
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = Strings.Confirm,
            SecondaryButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Secondary,
        };

        var result = _window != null
            ? await dialog.ShowAsync(_window)
            : await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}