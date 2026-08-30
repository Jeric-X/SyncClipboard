using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels.Sub;

namespace SyncClipboard.Core.ViewModels;

/// <summary>Clipboard copy and paste operations for history records.</summary>
public partial class HistoryViewModel
{
    private static readonly TimeSpan ForegroundActivationTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ForegroundCheckInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan PasteDispatchDelay = TimeSpan.FromMilliseconds(150);

    public async Task HandleCopyButtonAsync(HistoryRecordVM record, bool paste)
    {
        var operationName = paste ? "paste history record" : "copy history record";
        try
        {
            await RunWithOperationTimeoutAsync(
                operationName,
                token => CopyToClipboard(record, paste, token));
        }
        catch (Exception ex)
        {
            await logger.WriteAsync($"Failed to {operationName}:", ex.Message);
        }
    }

    public async Task CopyToClipboard(HistoryRecordVM record, bool paste, CancellationToken token)
    {
        var historyRecord = record.ToHistoryRecord();
        var profile = historyRecord.ToProfile();
        var valid = await profile.IsLocalDataValid(true, token);
        if (!valid)
        {
            historyRecord.IsLocalFileReady = false;
            await historyManager.UpdateHistoryLocalInfo(historyRecord, token);

            ShowWindowToastInfo(I18n.Strings.UnableToCopyByMissingFile);
            return;
        }

        if (!paste)
        {
            CloseHistoryWindowAfterCopyIfNeeded();
            await localClipboardSetter.Set(profile, token);
            return;
        }

        await localClipboardSetter.Set(profile, token);
        await PasteToLastExternalWindowAsync(token);
    }

    private void CloseHistoryWindowAfterCopyIfNeeded()
    {
        if (IsTopmost)
        {
            return;
        }

        ClearSelectedItem();
        window.ScrollToTop();
        window.Hide();
    }

    private async Task PasteToLastExternalWindowAsync(CancellationToken token)
    {
        if (!IsTopmost)
        {
            ClearSelectedItem();
            window.ScrollToTop();
            window.Hide();
            await PasteAfterHistoryWindowHiddenOrClosedAsync(token);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await PasteWithTemporarilyHiddenHistoryWindowAsync(token);
            return;
        }

        var activationRequested = _foregroundWindowTrackingService.TryActivateLastExternalWindow();
        if (activationRequested)
        {
            if (await AsyncRunner.WaitForConditionAsync(
                _foregroundWindowTrackingService.IsLastActivationTargetForeground,
                ForegroundActivationTimeout,
                ForegroundCheckInterval,
                token))
            {
                logger.Write("Paste target became foreground; sending paste shortcut.");
                keyboard.Paste();
                // Native key events are consumed asynchronously on macOS. Do not
                // take focus back until the target has received the full shortcut.
                await Task.Delay(PasteDispatchDelay, CancellationToken.None);
                window.Show(activate: true);
                logger.Write("History window focus restored after paste.");
                return;
            }

            logger.Write("Foreground window activation was accepted but could not be confirmed; using paste fallback.");
        }

        await PasteWithTemporarilyHiddenHistoryWindowAsync(token);
    }

    private async Task PasteWithTemporarilyHiddenHistoryWindowAsync(CancellationToken token)
    {
        var pasted = await RunTemporarilyHiddenAsync(async () =>
        {
            logger.Write("History window hidden for paste.");
            await PasteAfterHistoryWindowHiddenOrClosedAsync(token);
        }, token);
        if (!pasted)
        {
            logger.Write("Paste was canceled because the history window could not be hidden.");
            return;
        }
        logger.Write("History window restored after paste.");
    }

    private async Task<bool> RunTemporarilyHiddenAsync(
        Func<Task> operation,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(operation);
        token.ThrowIfCancellationRequested();
        if (!window.IsVisible)
        {
            return false;
        }

        var wasActive = window.IsActive;
        window.Hide();
        try
        {
            await operation();
            return true;
        }
        finally
        {
            window.Show(wasActive);
        }
    }

    private async Task PasteAfterHistoryWindowHiddenOrClosedAsync(CancellationToken token)
    {
        await Task.Delay(GetPasteDelayAfterWindowHidden(), token);
        logger.Write("History window hidden or closed; sending fallback paste shortcut.");
        keyboard.Paste();
        // SharpHook posts native keyboard events synchronously, but macOS handles
        // them asynchronously. Keep the history window hidden until the target
        // has had enough time to consume the complete key-down/key-up sequence.
        await Task.Delay(PasteDispatchDelay, CancellationToken.None);
        logger.Write("Fallback paste dispatch delay completed.");
    }

    private TimeSpan GetPasteDelayAfterWindowHidden()
    {
        var milliseconds = runtimeConfig
            .GetConfig<HistoryWindowConfig>()
            .PasteDelayAfterWindowHiddenMilliseconds;
        return TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
    }
}
