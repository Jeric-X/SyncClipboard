#if DISABLE_XAML_GENERATED_MAIN

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SyncClipboard.Core.Utilities;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using WinRT;

namespace SyncClipboard.WinUI3;

public static partial class Program
{
    [LibraryImport("Microsoft.ui.xaml.dll")]
    private static partial void XamlCheckProcessRequirements();

    [STAThread]
    private static void Main(string[] args)
    {
        if (AppInstance.EnsureSingleInstance(args) is false)
        {
            return;
        }

        try
        {
            XamlCheckProcessRequirements();
            ComWrappersSupport.InitializeComWrappers();
            Application.Start(delegate
            {
                DispatcherQueueSynchronizationContext synchronizationContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                var _ = new App();
            });
        }
        catch (Exception e)
        {
            App.Current?.LogUnhandledException(e);
        }
    }
}

#endif