using Avalonia;

namespace SyncClipboard.Updater;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args is ["--self-test"]) return SmokeChecks.CheckProtocol();
            if (args is ["--help"])
            {
                Console.WriteLine("SyncClipboard.Updater --task <absolute request.json path>");
                return 0;
            }
            if (args is ["--task", var path])
            {
                if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("The task path must be absolute.");
                UpdaterApplication.TaskPath = Path.GetFullPath(path);
            }
            else if (args is ["--smoke-test"]) UpdaterApplication.SmokeTest = true;
            else if (args.Length != 0) throw new ArgumentException("Use --task <request.json> or --help.");

            NativeLibraries.Register();
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
