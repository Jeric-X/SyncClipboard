using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SyncClipboard.Updater;

internal sealed class UpdateWindow : Window, IUpdateInteraction
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 12 };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private readonly UpdaterText text = UpdaterText.Current;
    private bool running;

    public UpdateWindow(bool smokeTest, UpdateArguments? update, IClassicDesktopStyleApplicationLifetime desktop)
    {
        Title = text.Title;
        Width = 620;
        Height = 360;
        MinWidth = 460;
        MinHeight = 280;
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 20,
                Children = { new TextBlock { Text = text.Title, FontSize = 20 }, status, progress, message, actions }
            }
        };
        status.Text = text.StartFromApplication;
        Closing += (_, _) =>
        {
            // Closing the updater means forced termination, just like closing the Windows console.
            if (running)
                Environment.Exit(3);
        };
        Opened += async (_, _) =>
        {
            if (smokeTest)
            {
                await Task.Delay(1000);
                Console.WriteLine("GUI_SMOKE=PASS");
                desktop.Shutdown(0);
            }
            else if (update is not null)
            {
                running = true;
                var result = await Task.Run(() => UpdateWorker.RunAsync(update, this, CancellationToken.None));
                running = false;
                desktop.Shutdown(result);
            }
        };
    }

    public void Report(string phase, int percent) => Dispatcher.UIThread.Post(() =>
    {
        status.Text = phase switch
        {
            "preparing" => text.Preparing,
            "waiting" => text.Waiting,
            "backup" => text.BackingUp,
            "installing" => text.Installing,
            "restoring" => text.Restoring,
            _ => phase
        };
        progress.IsIndeterminate = percent < 0;
        progress.Value = Math.Clamp(percent, 0, 100);
    });

    public Task<ForceExitAction> ConfirmForceExitAsync(CancellationToken token)
        => ChooseAsync(text.ConfirmForceExit,
            [(text.Yes, ForceExitAction.Yes), (text.No, ForceExitAction.No), (text.Retry, ForceExitAction.Retry)], token);

    public Task<UpdateFailureAction> AskFailureActionAsync(string path, Exception error, bool canRollback, CancellationToken token)
    {
        (string, UpdateFailureAction)[] choices = canRollback
            ? [
                (text.Retry, UpdateFailureAction.Retry),
                (text.AbortKeepBackup, UpdateFailureAction.Abort),
                (text.Rollback, UpdateFailureAction.Rollback)
            ]
            : [(text.Retry, UpdateFailureAction.Retry), (text.Abort, UpdateFailureAction.Abort)];
        return ChooseAsync(path + "\n\n" + error.Message, choices, token);
    }

    public async Task ShowResultAsync(UpdateResult result)
    {
        if (result.ExitCode == 0)
            return;
        var details = result.CleanupIncomplete ? text.CleanupIncomplete : text.Failed;
        details += "\n\n" + result.Error;
        if (result.BackupPath is not null)
            details += "\n\n" + text.BackupDirectory + result.BackupPath;
        if (result.WorkDirectory is not null)
            details += "\n\n" + text.Workspace + result.WorkDirectory;
        await ChooseAsync(details, [(text.Close, true)], CancellationToken.None);
    }

    private Task<T> ChooseAsync<T>(string question, (string Label, T Value)[] choices, CancellationToken token)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            progress.IsIndeterminate = false;
            message.Text = question;
            actions.Children.Clear();
            foreach (var (label, value) in choices)
            {
                var button = new Button { Content = label, IsDefault = actions.Children.Count == 0 };
                button.Click += (_, _) =>
                {
                    actions.Children.Clear();
                    message.Text = string.Empty;
                    completion.TrySetResult(value);
                };
                actions.Children.Add(button);
            }
            if (actions.Children.FirstOrDefault() is Button first)
                first.Focus();
        });
        return completion.Task.WaitAsync(token);
    }
}
