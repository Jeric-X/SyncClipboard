using SyncClipboard.Core.Interfaces;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Services;

public sealed class AvaloniaGlobalDialog : IGlobalDialog
{
    public async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText)
    {
        var dialog = new AvaloniaGlobalDialogWindow(
            title,
            message,
            primaryButtonText,
            closeButtonText);
        return await dialog.ShowAsync();
    }

    public async Task ShowMessageAsync(string title, string message, string closeButtonText)
    {
        var dialog = new AvaloniaGlobalDialogWindow(
            title,
            message,
            primaryButtonText: null,
            closeButtonText);
        await dialog.ShowAsync();
    }
}
