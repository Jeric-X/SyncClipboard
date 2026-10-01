using Sharprompt;
using Sharprompt.Drivers;
using System.Diagnostics;

namespace SyncClipboard.Updater;

internal sealed class ConsoleUpdateInteraction : IUpdateInteraction
{
    private readonly UpdaterText text;
    private readonly bool isElevated;
    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly bool waitForAcknowledgement;
    private bool usePrompts;

    public ConsoleUpdateInteraction(string language, bool isElevated = false, TextReader? input = null, TextWriter? output = null)
    {
        text = UpdaterText.ForLanguage(language);
        this.isElevated = isElevated;
        this.input = input ?? Console.In;
        this.output = output ?? Console.Out;
        waitForAcknowledgement = input is null && !Console.IsInputRedirected;
        usePrompts = input is null && output is null && !Console.IsInputRedirected && !Console.IsOutputRedirected;
        if (usePrompts)
        {
            Prompt.ConsoleDriverFactory = () => new UpdaterConsoleDriver(new DefaultConsoleDriver());
            Prompt.ThrowExceptionOnCancel = true;
        }
    }

    public void Report(string phase, int percent)
    {
        var message = phase switch
        {
            "preparing" => text.Preparing,
            "waiting" => text.Waiting,
            "backup" => text.BackingUp,
            "installing" => text.Installing,
            "restoring" => text.Restoring,
            _ => phase
        };
        WriteLine(percent < 0 ? message : $"{message}: {percent}%");
    }

    public async Task<ForceExitAction> ConfirmForceExitAsync(CancellationToken token)
    {
        var message = text.ConfirmForceExit;
        string[] choices = [text.Yes, text.No, text.Retry];
        if (TryPrompt(() => Prompt.Select(message, choices, defaultValue: choices[0]), out var selected))
            return selected == choices[0] ? ForceExitAction.Yes : selected == choices[1] ? ForceExitAction.No : ForceExitAction.Retry;
        WriteLine(message + " [Y/n/r]");
        while (true)
        {
            var answer = (await input.ReadLineAsync(token))?.Trim();
            if (answer is null)
                return ForceExitAction.No;
            if (answer.Length == 0 || answer.Equals("y", StringComparison.OrdinalIgnoreCase)
                || answer.Equals("yes", StringComparison.OrdinalIgnoreCase) || answer == "是")
                return ForceExitAction.Yes;
            if (answer.Equals("n", StringComparison.OrdinalIgnoreCase)
                || answer.Equals("no", StringComparison.OrdinalIgnoreCase) || answer == "否")
                return ForceExitAction.No;
            if (answer.Equals("r", StringComparison.OrdinalIgnoreCase)
                || answer.Equals("retry", StringComparison.OrdinalIgnoreCase) || answer == "重试")
                return ForceExitAction.Retry;
            WriteLine(text.EnterYesNoRetry);
        }
    }

    public async Task<UpdateFailureAction> AskFailureActionAsync(string path, Exception error, bool canRollback, CancellationToken token)
    {
        WriteLine($"{path}: {error.Message}");
        string[] choices = canRollback
            ? [text.Retry, text.Rollback, text.AbortKeepBackup]
            : [text.Retry, text.Abort];
        var message = text.ChooseAction;
        if (TryPrompt(() => Prompt.Select(message, choices, defaultValue: choices[0]), out var selected))
            return selected == choices[0] ? UpdateFailureAction.Retry
                : canRollback && selected == choices[1] ? UpdateFailureAction.Rollback : UpdateFailureAction.Abort;
        WriteLine(canRollback ? text.RecoveryMenu : text.FailureMenu);
        while (true)
        {
            switch ((await input.ReadLineAsync(token))?.Trim())
            {
                case "1":
                    return UpdateFailureAction.Abort;
                case "":
                case "2":
                    return UpdateFailureAction.Retry;
                case "3" when canRollback:
                    return UpdateFailureAction.Rollback;
                case null:
                    return canRollback ? UpdateFailureAction.Rollback : UpdateFailureAction.Abort;
                default:
                    WriteLine(canRollback ? text.EnterRecoveryAction : text.EnterFailureAction);
                    break;
            }
        }
    }

    public async Task ShowResultAsync(UpdateResult result)
    {
        if (result.ExitCode == 0)
        {
            WriteLine(text.Completed);
            return;
        }
        WriteLine(result.Error ?? text.Failed);
        if (result.BackupPath is not null)
            WriteLine(text.BackupDirectory + result.BackupPath);
        if (result.WorkDirectory is not null)
            WriteLine(text.Workspace + result.WorkDirectory);
        if ((!isElevated || result.BackupPath is not null) && waitForAcknowledgement)
        {
            var message = text.PressEnterToClose;
            if (TryPrompt(() => Prompt.Select<string>(message, [text.Close]), out _))
                return;
            WriteLine(text.PressEnterToClose);
            try
            {
                await input.ReadLineAsync();
            }
            catch (IOException error)
            {
                Debug.WriteLine(error);
            }
        }
    }

    private bool TryPrompt<T>(Func<T> prompt, out T? result)
    {
        result = default;
        if (!usePrompts)
            return false;
        try
        {
            result = prompt();
            return true;
        }
        catch (PromptCanceledException)
        {
            // Ctrl+C is process termination, never a request to roll back the installation.
            Environment.Exit(130);
            throw;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or TypeInitializationException)
        {
            Debug.WriteLine(error);
            usePrompts = false;
            return false;
        }
    }

    private void WriteLine(string text)
    {
        try
        {
            output.WriteLine(text);
        }
        catch (IOException error)
        {
            Debug.WriteLine(error);
        }
    }
}
