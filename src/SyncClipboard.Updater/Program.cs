#if UPDATER_AVALONIA
using Avalonia;
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
            if (args.Length != 0 && args is not ["--smoke-test"])
            {
                if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
                    throw new PlatformNotSupportedException(UpdaterText.Current.UnsupportedPlatform);
                var update = UpdateArguments.Parse(args);
                if (OperatingSystem.IsWindows())
                {
                    var interaction = new ConsoleUpdateInteraction(update.Language);
                    return UpdateWorker.RunAsync(update, interaction, CancellationToken.None).GetAwaiter().GetResult();
                }
#if UPDATER_AVALONIA
                UpdaterApplication.Update = update;
#else
                throw new PlatformNotSupportedException(UpdaterText.Current.UnsupportedPlatform);
#endif
            }

#if UPDATER_AVALONIA
            UpdaterApplication.SmokeTest = args is ["--smoke-test"];
            return AppBuilder.Configure<UpdaterApplication>().UsePlatformDetect()
                .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
                .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
                .StartWithClassicDesktopLifetime([]);
#else
            Console.WriteLine(UpdaterText.Current.Title);
            Console.WriteLine(UpdaterText.Current.StartFromApplication);
            if (args is ["--smoke-test"]) Console.WriteLine("CONSOLE_SMOKE=PASS");
            return 0;
#endif
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
