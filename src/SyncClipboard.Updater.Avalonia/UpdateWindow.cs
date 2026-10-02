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
    private readonly CancellationTokenSource cancellation = new();
    private Window? closeDialog;
    private Button? closeRollbackButton;
    private volatile bool rollbackAvailable;
    private bool running;
    private int exitCode;

    public UpdateWindow(string[] args, IClassicDesktopStyleApplicationLifetime desktop)
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
        desktop.Exit += (_, e) =>
        {
            if (exitCode != 0)
                e.ApplicationExitCode = exitCode;
        };
        Closing += OnClosing;
        Opened += async (_, _) => await RunUpdateAsync(args, desktop);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!running)
            return;
        e.Cancel = true;
        if (closeDialog is not null)
            return;
        try
        {
            var choice = await ConfirmCloseAsync();
            if (!running)
                return;
            if (choice == UpdateFailureAction.Abort)
                Environment.Exit(3);
            if (choice == UpdateFailureAction.Rollback && rollbackAvailable)
            {
                SetRollbackAvailable(false);
                actions.Children.Clear();
                message.Text = string.Empty;
                status.Text = text.Restoring;
                progress.IsIndeterminate = true;
                await cancellation.CancelAsync();
            }
        }
        catch (Exception error)
        {
            Program.ShowFatalError(error);
        }
    }

    private async Task RunUpdateAsync(string[] args, IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            if (args is ["--smoke-test"])
            {
                await Task.Delay(1000);
                Console.WriteLine("GUI_SMOKE=PASS");
                desktop.Shutdown(0);
            }
            else if (args.Length != 0)
            {
                running = true;
                exitCode = await Task.Run(() => UpdateRunner.RunAsync(args, this, cancellation.Token));
                running = false;
                closeDialog?.Close();
                desktop.Shutdown(exitCode);
            }
        }
        catch (Exception error)
        {
            running = false;
            closeDialog?.Close();
            Program.ShowFatalError(error);
            desktop.Shutdown(1);
        }
    }

    private async Task<UpdateFailureAction?> ConfirmCloseAsync()
    {
        var dialog = new Window
        {
            Title = text.Title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        (string Label, UpdateFailureAction? Action)[] choices =
        [
            (text.Abort, UpdateFailureAction.Abort),
            (text.Rollback, UpdateFailureAction.Rollback),
            (text.Cancel, null)
        ];
        foreach (var (label, action) in choices)
        {
            var button = new Button { Content = label, IsDefault = action is null, IsCancel = action is null };
            if (action == UpdateFailureAction.Rollback)
            {
                closeRollbackButton = button;
                button.IsVisible = rollbackAvailable;
            }
            button.Click += (_, _) => dialog.Close(action);
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children = { new TextBlock { Text = text.ConfirmClose, TextWrapping = TextWrapping.Wrap }, buttons }
        };
        closeDialog = dialog;
        try
        {
            return await dialog.ShowDialog<UpdateFailureAction?>(this);
        }
        finally
        {
            closeDialog = null;
            closeRollbackButton = null;
        }
    }

    public void SetRollbackAvailable(bool available)
    {
        rollbackAvailable = available;
        Dispatcher.UIThread.Post(() =>
        {
            closeRollbackButton?.IsVisible = rollbackAvailable;
        });
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
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            running = false;
            exitCode = result.ExitCode;
            status.Text = result.CleanupIncomplete ? text.CleanupIncomplete : text.Failed;
            closeDialog?.Close();
        });
        var details = result.Error ?? text.Failed;
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
