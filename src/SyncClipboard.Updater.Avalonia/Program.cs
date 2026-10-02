using Avalonia;
using SyncClipboard.Updater.Dmg;

namespace SyncClipboard.Updater;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (OperatingSystem.IsMacOS() && args is ["--replace-bundle", var prepared, var target])
                return MacBundleSwap.RunCommand(prepared, target);
            UpdaterText.Current = UpdaterText.FromArguments(args);
            if (args is ["--help"])
            {
                Console.WriteLine(UpdaterText.Current.Help);
                return 0;
            }
            UpdaterApplication.Arguments = args;
            return AppBuilder.Configure<UpdaterApplication>().UsePlatformDetect()
                .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
                .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
                .StartWithClassicDesktopLifetime([]);
        }
        catch (Exception error)
        {
            ShowFatalError(error);
            return 1;
        }
    }

    internal static void ShowFatalError(Exception error)
    {
        Console.Error.WriteLine(error);
        if (!OperatingSystem.IsMacOS())
            return;
        try
        {
            const string script = """
                on run argv
                    display alert (item 1 of argv) message (item 2 of argv) as critical buttons {item 3 of argv} default button 1
                end run
                """;
            MacCommand.RunAsync("/usr/bin/osascript",
                ["-e", script, UpdaterText.Current.Title, error.Message, UpdaterText.Current.Close], CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception presentationError)
        {
            Console.Error.WriteLine(presentationError);
        }
    }
}
