using SyncClipboard.Core.Interfaces;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;
using System;
using System.Threading.Tasks;

namespace SyncClipboard.WinUI3.Services;

public sealed class WinUIGlobalDialog : IGlobalDialog
{
    public Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText)
    {
        var buttonDescription = $"{primaryButtonText}: Yes{Environment.NewLine}{closeButtonText}: No";
        var result = MessageBox(
            HWND.NULL,
            $"{message}{Environment.NewLine}{Environment.NewLine}{buttonDescription}",
            title,
            MB_FLAGS.MB_YESNO
            | MB_FLAGS.MB_ICONERROR
            | MB_FLAGS.MB_DEFBUTTON2
            | MB_FLAGS.MB_SETFOREGROUND);
        return Task.FromResult(result == MB_RESULT.IDYES);
    }

    public Task ShowMessageAsync(string title, string message, string closeButtonText)
    {
        _ = MessageBox(
            HWND.NULL,
            message,
            title,
            MB_FLAGS.MB_OK | MB_FLAGS.MB_ICONERROR | MB_FLAGS.MB_SETFOREGROUND);
        return Task.CompletedTask;
    }
}
