using Avalonia;

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
            if (args is ["--smoke-test"]) UpdaterApplication.SmokeTest = true;
            else if (args.Length != 0) throw new ArgumentException("Use --help for available options.");

            return AppBuilder.Configure<UpdaterApplication>().UsePlatformDetect()
                .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
                .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
                .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
                .StartWithClassicDesktopLifetime([]);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
