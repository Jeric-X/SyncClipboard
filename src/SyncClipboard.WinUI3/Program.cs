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
        using var mutex = AppInstance.EnsureSingleInstance();
        if (mutex is null)
        {
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