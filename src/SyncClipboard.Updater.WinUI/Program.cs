using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using WinRT;

namespace SyncClipboard.Updater;

internal static partial class Program
{
    internal static int ExitCode { get; set; }

    [LibraryImport("Microsoft.ui.xaml.dll")]
    private static partial void XamlCheckProcessRequirements();

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint window, string message, string title, uint type);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            UpdaterText.Current = UpdaterText.FromArguments(args);
            if (args is ["--help"])
            {
                Console.WriteLine(UpdaterText.Current.Help);
                return 0;
            }
#if UPDATER_LOCAL_RUNTIME
            Console.WriteLine("WINDOWS_APP_SDK=local");
#else
            Console.WriteLine("WINDOWS_APP_SDK=installed");
#endif
            XamlCheckProcessRequirements();
            ComWrappersSupport.InitializeComWrappers();
            Application.Start(callback =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new UpdaterApplication(args);
            });
            return ExitCode;
        }
        catch (Exception error)
        {
            ShowFatalError(error, args is ["--smoke-test"]);
            return 1;
        }
    }

    internal static void ShowFatalError(Exception error, bool smokeTest = false)
    {
        Console.Error.WriteLine(error);
        if (!smokeTest)
            MessageBox(0, error.Message, UpdaterText.Current.Title, 0x10);
    }
}
