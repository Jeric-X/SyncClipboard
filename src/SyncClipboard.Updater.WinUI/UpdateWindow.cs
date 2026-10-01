using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using Windows.Graphics;

namespace SyncClipboard.Updater;

internal sealed partial class UpdateWindow : Window, IUpdateInteraction
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock message = new()
    {
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        IsTextSelectionEnabled = true
    };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100 };
    private readonly StackPanel actions = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly UpdaterText text = UpdaterText.Current;
    private readonly CancellationTokenSource cancellation = new();
    private ContentDialog? closeDialog;
    private volatile bool rollbackAvailable;
    private bool running;

    public UpdateWindow(string[] args)
    {
        Title = text.Title;
        AppWindow.Resize(new SizeInt32(680, 460));
        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        AppWindow.SetPresenter(presenter);
        var titleBar = new Grid
        {
            Height = 32,
            Padding = new Thickness(24, 0, 160, 0),
            Children = { new TextBlock { Text = text.Title, VerticalAlignment = VerticalAlignment.Center } }
        };
        var layout = new Grid
        {
            Padding = new Thickness(24),
            RowSpacing = 20,
            RowDefinitions =
            {
                new() { Height = GridLength.Auto }, new() { Height = GridLength.Auto },
                new() { Height = new GridLength(1, GridUnitType.Star) },
                new() { Height = GridLength.Auto }
            }
        };
        FrameworkElement[] controls = [status, progress, message, actions];
        for (var i = 0; i < controls.Length; i++)
        {
            Grid.SetRow(controls[i], i);
            layout.Children.Add(controls[i]);
        }
        var root = new Grid
        {
            RowDefinitions = { new() { Height = GridLength.Auto }, new() { Height = new GridLength(1, GridUnitType.Star) } },
            Children = { titleBar, layout }
        };
        Grid.SetRow(layout, 1);
        Content = root;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);
        AppWindow.TitleBar.ButtonBackgroundColor = default(Color);
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = default(Color);
        void UpdateTitleBar(FrameworkElement sender, object _)
        {
            AppWindow.TitleBar.ButtonForegroundColor = sender.ActualTheme == ElementTheme.Dark
                ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0);
        }
        root.ActualThemeChanged += UpdateTitleBar;
        root.Loaded += (sender, args) => UpdateTitleBar(root, args);
        status.Text = text.StartFromApplication;
        AppWindow.Closing += OnClosing;
        ((FrameworkElement)Content).Loaded += async (_, _) =>
        {
            try
            {
                if (args is ["--smoke-test"])
                {
                    Report("installing", 50);
                    var dialog = new ContentDialog
                    {
                        XamlRoot = Content.XamlRoot,
                        Title = text.Title,
                        Content = text.Installing,
                        CloseButtonText = text.Close
                    };
                    var shown = dialog.ShowAsync();
                    await Task.Delay(500);
                    // Exercise WinUI reference tracking while controls are alive, not just startup.
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
                    dialog.Hide();
                    await shown;
                    Console.WriteLine("GUI_SMOKE=PASS");
                    Close();
                }
                else if (args.Length != 0)
                {
                    running = true;
                    Program.ExitCode = await Task.Run(() => UpdateRunner.RunAsync(args, this, cancellation.Token));
                    running = false;
                    closeDialog?.Hide();
                    Close();
                }
            }
            catch (Exception error)
            {
                running = false;
                Program.ExitCode = 1;
                Program.ShowFatalError(error, args is ["--smoke-test"]);
                Close();
            }
        };
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!running)
            return;
        args.Cancel = true;
        if (closeDialog is not null)
            return;
        closeDialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = text.Title,
            Content = text.ConfirmClose,
            PrimaryButtonText = text.Abort,
            SecondaryButtonText = text.Rollback,
            IsSecondaryButtonEnabled = rollbackAvailable,
            CloseButtonText = text.Cancel,
            DefaultButton = ContentDialogButton.Close
        };
        try
        {
            var result = await closeDialog.ShowAsync();
            if (!running)
                return;
            if (result == ContentDialogResult.Primary)
                Environment.Exit(3);
            if (result == ContentDialogResult.Secondary && rollbackAvailable)
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
        finally
        {
            closeDialog = null;
        }
    }

    public void SetRollbackAvailable(bool available)
    {
        rollbackAvailable = available;
        DispatcherQueue.TryEnqueue(() =>
        {
            closeDialog?.IsSecondaryButtonEnabled = rollbackAvailable;
        });
    }

    public void Report(string phase, int percent) => DispatcherQueue.TryEnqueue(() =>
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
            ? [(text.Retry, UpdateFailureAction.Retry), (text.AbortKeepBackup, UpdateFailureAction.Abort),
                (text.Rollback, UpdateFailureAction.Rollback)]
            : [(text.Retry, UpdateFailureAction.Retry), (text.Abort, UpdateFailureAction.Abort)];
        return ChooseAsync(path + "\n\n" + error.Message, choices, token);
    }

    public async Task ShowResultAsync(UpdateResult result)
    {
        if (result.ExitCode == 0)
            return;
        DispatcherQueue.TryEnqueue(() =>
        {
            running = false;
            Program.ExitCode = result.ExitCode;
            status.Text = result.CleanupIncomplete ? text.CleanupIncomplete : text.Failed;
            closeDialog?.Hide();
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
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (token.IsCancellationRequested)
                return;
            progress.IsIndeterminate = false;
            message.Text = question;
            ToolTipService.SetToolTip(message, question);
            actions.Children.Clear();
            foreach (var (label, value) in choices)
            {
                var button = new Button { Content = label };
                button.Click += (_, _) =>
                {
                    actions.Children.Clear();
                    message.Text = string.Empty;
                    completion.TrySetResult(value);
                };
                actions.Children.Add(button);
            }
            if (actions.Children.FirstOrDefault() is Button first)
                first.Focus(FocusState.Programmatic);
        }))
            completion.TrySetException(new InvalidOperationException("The updater window has closed."));
        return completion.Task.WaitAsync(token);
    }
}
