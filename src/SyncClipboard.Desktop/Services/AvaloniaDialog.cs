using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Services;

public class AvaloniaDialog : IMainWindowDialog
{
    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = Strings.Confirm,
            SecondaryButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Secondary
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}