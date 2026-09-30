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
                Console.WriteLine("SyncClipboard.Updater [--smoke-test]\nWindows portable ZIP: --package-path <zip> --digest sha256:<hash> "
                    + "--target <directory> --executable <SyncClipboard.exe> --process-id <pid> "
                    + "[--language <language>] [--protect-path <path> ...]");
                return 0;
            }
            if (args.Length != 0 && args is not ["--smoke-test"])
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("ZIP installation is supported on Windows only.");
                if (args is ["--cleanup-work", var workspace, "--wait-pid", var pid, "--wait-start", var start])
                {
                    UpdateWorker.CleanupAsync(workspace, int.Parse(pid, System.Globalization.CultureInfo.InvariantCulture),
                        long.Parse(start, System.Globalization.CultureInfo.InvariantCulture)).GetAwaiter().GetResult();
                    return 0;
                }
                var update = UpdateArguments.Parse(args);
                var interaction = new ConsoleUpdateInteraction(update.Language, update.Elevated);
                return UpdateWorker.RunAsync(update, interaction, CancellationToken.None).GetAwaiter().GetResult();
            }

#if UPDATER_AVALONIA
            UpdaterApplication.SmokeTest = args is ["--smoke-test"];
            return AppBuilder.Configure<UpdaterApplication>().UsePlatformDetect()
                .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
                .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
                .StartWithClassicDesktopLifetime([]);
#else
            Console.WriteLine("SyncClipboard 更新助手 / Updater");
            Console.WriteLine("请从 SyncClipboard 启动更新。 / Start updates from SyncClipboard.");
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
