#if DISABLE_XAML_GENERATED_MAIN

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SyncClipboard.Core.Utilities;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using WinRT;

namespace SyncClipboard.WinUI3;

public static class Program
{
    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    private static void Main(string[] _)
    {
        Mutex mutex = new(false, WinUIEnv.SoftName, out bool creetedNew);
        if (!creetedNew)
        {
            AppInstance.ActiveOtherInstance(SyncClipboard.Core.Commons.Env.Appid).Wait();
            return;
        }

        XamlCheckProcessRequirements();
        ComWrappersSupport.InitializeComWrappers();
        Application.Start(delegate
        {
            DispatcherQueueSynchronizationContext synchronizationContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            var _ = new App();
        });
    }
}

#endif