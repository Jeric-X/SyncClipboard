using SyncClipboard.Core.Interfaces;
using System.Threading.Tasks;
using Vanara.InteropServices;
using static Vanara.PInvoke.ComCtl32;

namespace SyncClipboard.WinUI3.Services;

public sealed class WinUIGlobalDialog : IGlobalDialog
{
    private const int PrimaryButtonId = 100;
    private const int CloseButtonId = 101;

    public Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText)
    {
        var selectedButtonId = ShowTaskDialog(
            title,
            message,
            CloseButtonId,
            (PrimaryButtonId, primaryButtonText),
            (CloseButtonId, closeButtonText));
        return Task.FromResult(selectedButtonId == PrimaryButtonId);
    }

    public Task ShowMessageAsync(string title, string message, string closeButtonText)
    {
        ShowTaskDialog(title, message, CloseButtonId, (CloseButtonId, closeButtonText));
        return Task.CompletedTask;
    }

    private static int ShowTaskDialog(
        string title,
        string message,
        int defaultButtonId,
        params (int Id, string Text)[] buttons)
    {
        var buttonTexts = new SafeCoTaskMemString[buttons.Length];
        try
        {
            var nativeButtons = new TASKDIALOG_BUTTON[buttons.Length];
            for (var index = 0; index < buttons.Length; index++)
            {
                buttonTexts[index] = new SafeCoTaskMemString(buttons[index].Text);
                nativeButtons[index] = new TASKDIALOG_BUTTON
                {
                    nButtonID = buttons[index].Id,
                    pszButtonText = buttonTexts[index].DangerousGetHandle(),
                };
            }

            using var nativeButtonMemory = SafeCoTaskMemHandle.CreateFromList(nativeButtons);
            using var config = new TASKDIALOGCONFIG
            {
                WindowTitle = title,
                Content = message,
                dwFlags = TASKDIALOG_FLAGS.TDF_ALLOW_DIALOG_CANCELLATION
                    | TASKDIALOG_FLAGS.TDF_SIZE_TO_CONTENT,
                mainIcon = (nint)TaskDialogIcon.TD_ERROR_ICON,
                cButtons = (uint)nativeButtons.Length,
                pButtons = nativeButtonMemory.DangerousGetHandle(),
                nDefaultButton = defaultButtonId,
            };

            TaskDialogIndirect(config, out var selectedButtonId, out _, out _).ThrowIfFailed();
            return selectedButtonId;
        }
        finally
        {
            foreach (var buttonText in buttonTexts)
            {
                buttonText?.Dispose();
            }
        }
    }
}
