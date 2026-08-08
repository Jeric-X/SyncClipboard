using Avalonia;
using Avalonia.Media;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Desktop.Default;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        if (StartUpHelper.TryUpdateWindowsStartupTaskFromArguments(args, out var returnCode))
        {
            return returnCode;
        }

        if (AppInstance.EnsureSingleInstance(args) is false)
        {
            return (int)ReturnCode.Success;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            var path = Path.Combine(Env.LogFolder, $"{DateTime.Now:yyyy-MM-dd HH-mm-ss}.dmp");
            File.WriteAllText(path + ".txt", $"UnhandledException {e.GetType()} {e.Message} \n{e.StackTrace}");
            App.Current?.Logger?.Write($"UnhandledException {e.GetType()} {e.Message} \n {e.StackTrace}");
            App.Current?.AppCore?.Stop();
            return (int)ReturnCode.UnhandledException;
        }

        return (int)ReturnCode.Success;
    }

    private static string Font(string name)
    {
        return $"avares://SyncClipboard.Desktop.Default/Assets/Fonts#{name}";
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure(() => new App(AppServices.ConfigureServices()))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = $"{Font("MiSans")}",
            });
}
