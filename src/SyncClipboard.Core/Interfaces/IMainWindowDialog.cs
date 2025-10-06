namespace SyncClipboard.Core.Interfaces;

public interface IMainWindowDialog
{
    Task<bool> ShowConfirmationAsync(string title, string message);
}