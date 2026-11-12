using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SyncClipboard.Test.WinUI3.App;

// XAML resolves PRI resources relative to the executable. Keep native UI checks
// in this process instead of initializing Application in VSTest's testhost.exe.
internal static class UITestHost
{
    [DllImport("Microsoft.UI.Xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2)
            return 1;

        TestApplication? app = null;
        try
        {
            var type = typeof(UITestHost).Assembly.GetType(args[0], throwOnError: true)!;
            var method = type.GetMethod(args[1], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: Type.EmptyTypes, modifiers: null)
                ?? throw new MissingMethodException(type.FullName, args[1]);
            var test = method.CreateDelegate<Func<Task>>();
            XamlCheckProcessRequirements();
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                var dispatcher = DispatcherQueue.GetForCurrentThread();
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(dispatcher));
                app = new TestApplication { RunTest = test };
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        return app?.ExitCode ?? 1;
    }
}
