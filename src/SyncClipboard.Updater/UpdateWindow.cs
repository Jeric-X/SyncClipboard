using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SyncClipboard.Updater.Core;

namespace SyncClipboard.Updater;

internal sealed class UpdateWindow : Window
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly TextBlock status = new() { FontSize = 18, Foreground = Brushes.Black };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Black };
    private readonly Border fill = new() { Background = Brushes.DodgerBlue, Width = 0, HorizontalAlignment = HorizontalAlignment.Left };
    private bool chinese;
    private bool finished;

    public UpdateWindow(string? taskPath, bool smokeTest, IClassicDesktopStyleApplicationLifetime desktop)
    {
        Title = "SyncClipboard Update";
        Width = 520;
        Height = 250;
        CanResize = false;
        Background = Brushes.White;
        status.Text = "SyncClipboard Update / 更新";
        details.Text = "Please start this updater from SyncClipboard. / 请从 SyncClipboard 启动更新。";
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16,
            Children = { status, new Border { Height = 10, Background = Brushes.LightGray, Child = fill }, details }
        };
        Closing += (_, _) => cancellation.Cancel();
        Opened += async (_, _) =>
        {
            if (smokeTest)
            {
                await Task.Delay(500);
                ShowProgress(new(UpdatePhase.Preparing, 50));
                await Task.Delay(500);
                Console.WriteLine("GUI_SMOKE=PASS");
                desktop.Shutdown(0);
            }
            else if (taskPath is not null) await RunTaskAsync(taskPath, desktop);
        };
    }

    private async Task RunTaskAsync(string path, IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var work = Path.GetDirectoryName(path)!;
            // One helper owns each task. Completed artifacts are never silently reused.
            using var ownership = new FileStream(Path.Combine(work, "run.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            if (File.Exists(Path.Combine(work, "result.json"))) throw new IOException("This update task already has a result.");
            var request = await UpdateTaskFile.ReadAsync(path, cancellation.Token);
            chinese = request.Language?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
            details.Text = request.TargetVersion;
            var progress = new Progress<UpdateProgress>(value => Dispatcher.UIThread.Post(() => ShowProgress(value)));
            // Platform strategies are intentionally absent in this first framework PR.
            var runner = new UpdateRunner(new UpdateStrategyFactory([]));
            var result = await Task.Run(() => runner.RunAsync(request, progress, cancellation.Token));
            finished = true;
            await UpdateTaskFile.WriteResultAsync(work, result);
            if (result.Outcome == UpdateOutcome.Succeeded) desktop.Shutdown(0);
            else ShowFailure(result.Error ?? (chinese ? "更新已取消" : "Update canceled"));
        }
        catch (Exception error)
        {
            ShowFailure(error.Message);
            Console.Error.WriteLine(error);
        }
    }

    private void ShowProgress(UpdateProgress progress)
    {
        if (finished) return;
        status.Text = UpdaterText.Phase(progress.Phase, chinese);
        if (progress.Percentage is double percentage && double.IsFinite(percentage))
            fill.Width = 450 * Math.Clamp(percentage, 0, 100) / 100;
    }

    private void ShowFailure(string message)
    {
        finished = true;
        status.Text = chinese ? "更新未完成" : "Update did not complete";
        details.Text = message;
    }
}
