using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace SyncClipboard.Test.WinUI3;

// XAML resolves PRI resources relative to the executable, so UI tests run in this
// dedicated host instead of initializing Application inside VSTest's testhost.exe.
internal static class HistoryImageTestHost
{
    [DllImport("Microsoft.UI.Xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is not ["--history-image-test"])
            return 1;

        var exitCode = 1;
        try
        {
            XamlCheckProcessRequirements();
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                var dispatcher = DispatcherQueue.GetForCurrentThread();
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(dispatcher));
                var app = new ImageTestApplication();
                dispatcher.TryEnqueue(async () =>
                {
                    try
                    {
                        await HistoryImageTests.RunImageChecksAsync();
                        exitCode = 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex);
                    }
                    finally
                    {
                        app.Exit();
                    }
                });
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
        return exitCode;
    }
}
