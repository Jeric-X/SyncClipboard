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
            if (args is ["--help"])
            {
                Console.WriteLine("SyncClipboard.Updater [--smoke-test]");
                return 0;
            }
            if (args.Length != 0 && args is not ["--smoke-test"])
                throw new ArgumentException("Use --help for available options.");

#if UPDATER_AVALONIA
            UpdaterApplication.SmokeTest = args is ["--smoke-test"];
            return AppBuilder.Configure<UpdaterApplication>().UsePlatformDetect()
                .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
                .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
                .StartWithClassicDesktopLifetime([]);
#else
            Console.WriteLine("SyncClipboard 更新助手 / Updater");
            Console.WriteLine("更新功能尚未接入。 / Installation is not implemented yet.");
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
