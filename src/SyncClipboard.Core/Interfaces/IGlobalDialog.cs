namespace SyncClipboard.Core.Interfaces;

public interface IGlobalDialog
{
    Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText);

    Task ShowMessageAsync(string title, string message, string closeButtonText);
}
