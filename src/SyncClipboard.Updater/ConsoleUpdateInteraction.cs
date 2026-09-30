using Sharprompt;
using Sharprompt.Drivers;
using System.Diagnostics;

namespace SyncClipboard.Updater;

internal sealed class ConsoleUpdateInteraction : IUpdateInteraction
{
    private readonly bool chinese;
    private readonly bool isElevated;
    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly bool waitForAcknowledgement;
    private bool usePrompts;

    public ConsoleUpdateInteraction(string language, bool isElevated = false, TextReader? input = null, TextWriter? output = null)
    {
        chinese = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
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
        var text = chinese ? phase switch
        {
            "preparing" => "复制并校验更新包",
            "waiting" => "等待程序退出",
            "backup" => "备份程序文件",
            "installing" => "安装更新",
            "restoring" => "恢复旧版本",
            _ => phase
        } : phase;
        WriteLine(percent < 0 ? text : $"{text}: {percent}%");
    }

    public async Task<bool> ConfirmForceExitAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var message = chinese
            ? "主程序等待 10 秒仍未退出。是否强制退出并继续更新？未保存的内容可能丢失。"
            : "SyncClipboard has not exited after 10 seconds. Force it to exit and continue updating? Unsaved changes may be lost.";
        if (TryPrompt(() => Prompt.Confirm(message, defaultValue: true), out var confirmed))
            return confirmed;
        WriteLine(message + " [Y/n]");
        while (true)
        {
            var answer = (await input.ReadLineAsync(token))?.Trim();
            if (answer is null)
                return false;
            if (answer.Length == 0 || answer.Equals("y", StringComparison.OrdinalIgnoreCase)
                || answer.Equals("yes", StringComparison.OrdinalIgnoreCase) || answer == "是")
                return true;
            if (answer.Equals("n", StringComparison.OrdinalIgnoreCase)
                || answer.Equals("no", StringComparison.OrdinalIgnoreCase) || answer == "否")
                return false;
            WriteLine(chinese ? "请输入 y 或 n。" : "Enter y or n.");
        }
    }

    public async Task<UpdateFailureAction> AskFailureActionAsync(string path, Exception error, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        WriteLine($"{path}: {error.Message}");
        string[] choices = chinese
            ? ["重试", "回滚，恢复旧版本", "终止，不回滚，保留备份"]
            : ["Retry", "Roll back to the previous version", "Abort without rollback; keep backups"];
        var message = chinese ? "请选择操作" : "Choose an action";
        if (TryPrompt(() => Prompt.Select(message, choices, defaultValue: choices[0]), out var selected))
            return selected == choices[0] ? UpdateFailureAction.Retry
                : selected == choices[1] ? UpdateFailureAction.Rollback : UpdateFailureAction.Abort;
        WriteLine(chinese
            ? "[1] 终止（不回滚，保留备份，不启动主程序） [2] 重试（默认） [3] 回滚"
            : "[1] Abort (no rollback; keep backups; do not start the application) [2] Retry (default) [3] Roll back");
        while (true)
        {
            switch ((await input.ReadLineAsync(token))?.Trim())
            {
                case "1":
                    return UpdateFailureAction.Abort;
                case "":
                case "2":
                    return UpdateFailureAction.Retry;
                case "3":
                case null:
                    return UpdateFailureAction.Rollback;
                default:
                    WriteLine(chinese ? "请输入 1、2 或 3。" : "Enter 1, 2, or 3.");
                    break;
            }
        }
    }

    public async Task ShowResultAsync(UpdateResult result)
    {
        if (result.ExitCode == 0)
        {
            WriteLine(chinese ? "更新完成。" : "Update completed.");
            return;
        }
        WriteLine(result.Error ?? (chinese ? "更新失败。" : "Update failed."));
        if (result.BackupPath is not null)
            WriteLine((chinese ? "备份目录: " : "Backup directory: ") + result.BackupPath);
        if (result.WorkDirectory is not null)
            WriteLine((chinese ? "日志及工作目录: " : "Log and workspace: ") + result.WorkDirectory);
        if ((!isElevated || result.BackupPath is not null) && waitForAcknowledgement)
        {
            var message = chinese ? "按回车关闭" : "Press Enter to close";
            if (TryPrompt(() => Prompt.Select<string>(message, [chinese ? "关闭" : "Close"]), out _))
                return;
            WriteLine(chinese ? "按回车关闭。" : "Press Enter to close.");
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
