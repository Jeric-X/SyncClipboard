using System.Diagnostics;
#if UPDATER_AVALONIA
using Avalonia;
using SyncClipboard.Updater.Dmg;
#endif

namespace SyncClipboard.Updater;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            UpdaterText.Current = UpdaterText.FromArguments(args);
            if (args is ["--help"])
            {
                Console.WriteLine(UpdaterText.Current.Help);
                return 0;
            }
            if (OperatingSystem.IsWindows() && args.Length != 0 && args is not ["--smoke-test"])
            {
                var interaction = new ConsoleUpdateInteraction(UpdaterText.Current);
                return RunUpdateAsync(args, interaction, CancellationToken.None).GetAwaiter().GetResult();
            }
#if UPDATER_AVALONIA
            UpdaterApplication.Arguments = args;
            return AppBuilder.Configure<UpdaterApplication>().UsePlatformDetect()
                .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
                .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
                .StartWithClassicDesktopLifetime([]);
#else
            Console.WriteLine(UpdaterText.Current.Title);
            Console.WriteLine(UpdaterText.Current.StartFromApplication);
            if (args is ["--smoke-test"])
                Console.WriteLine("CONSOLE_SMOKE=PASS");
            return 0;
#endif
        }
        catch (Exception error)
        {
            ShowFatalError(error);
            return 1;
        }
    }

    internal static async Task<int> RunUpdateAsync(string[] args, IUpdateInteraction interaction, CancellationToken token)
    {
        UpdateArguments? update = null;
        try
        {
            UpdaterText.Current = UpdaterText.FromArguments(args);
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
                throw new PlatformNotSupportedException(UpdaterText.Current.UnsupportedPlatform);
            update = UpdateArguments.Parse(args);
            return await UpdateWorker.RunAsync(update, interaction, token);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            await interaction.ShowResultAsync(new UpdateResult(1, error.Message, update?.WorkDirectory));
            return 1;
        }
    }

    internal static void ShowFatalError(Exception error)
    {
        Console.Error.WriteLine(error);
        try
        {
#if UPDATER_AVALONIA
            if (OperatingSystem.IsMacOS())
            {
                const string script = """
                    on run argv
                        display alert (item 1 of argv) message (item 2 of argv) as critical buttons {item 3 of argv} default button 1
                    end run
                    """;
                MacCommand.RunAsync("/usr/bin/osascript",
                    ["-e", script, UpdaterText.Current.Title, error.Message, UpdaterText.Current.Close], CancellationToken.None)
                    .GetAwaiter().GetResult();
                return;
            }
#endif
            new ConsoleUpdateInteraction(UpdaterText.Current).ShowResultAsync(new UpdateResult(1, error.Message))
                .GetAwaiter().GetResult();
        }
        catch (Exception presentationError)
        {
            // Keep both errors available even if the platform cannot present a dialog or console prompt.
            Debug.WriteLine(presentationError);
            Console.Error.WriteLine(presentationError);
        }
    }
}
