namespace SyncClipboard.Core.Interfaces;

public interface IMainWindowDialog
{
    Task<bool> ShowConfirmationAsync(string title, string message);
    Task ShowMessageAsync(string title, string message);
    Task<bool?> ShowThreeButtonConfirmationAsync(string title, string message, string primaryText, string secondaryText, string closeText);
}