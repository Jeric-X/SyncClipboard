namespace SyncClipboard.Core.Utilities.Updater;

internal static class WindowsUpdaterFiles
{
    // Copy only native UI dependencies, never the main application's managed assemblies or CLR.
    // Framework-dependent WinUI builds use the installed SDK through the copied bootstrap DLL.
    private static readonly string[] Libraries =
    [
        "CoreMessagingXP.dll", "dcompi.dll", "dwmcorei.dll", "DwmSceneI.dll", "DWriteCore.dll", "marshal.dll",
        "Microsoft.DirectManipulation.dll", "Microsoft.InputStateManager.dll", "Microsoft.Internal.FrameworkUdk.dll",
        "Microsoft.UI.Composition.OSSupport.dll", "Microsoft.UI.Input.dll", "Microsoft.UI.Windowing.Core.dll",
        "Microsoft.UI.Windowing.dll", "Microsoft.UI.Xaml.Controls.dll", "Microsoft.UI.Xaml.dll",
        "Microsoft.UI.Xaml.Internal.dll", "Microsoft.UI.Xaml.resources.19h1.dll", "Microsoft.UI.Xaml.resources.common.dll",
        "Microsoft.Windows.ApplicationModel.Resources.dll", "Microsoft.WindowsAppRuntime.dll",
        "Microsoft.WindowsAppRuntime.Bootstrap.dll", "MRM.dll", "wuceffectsi.dll",
        "libSkiaSharp.dll", "libHarfBuzzSharp.dll"
    ];

    internal static IEnumerable<(string Source, string Name)> GetDependencies(string directory)
    {
        foreach (var name in Libraries)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
                yield return (path, name);
        }
        // The merged main-app PRI includes WinUI's theme resources. A pure-code updater has
        // no PRI of its own; MRT resolves this existing content under the conventional name.
        var resources = Path.Combine(directory, "SyncClipboard.pri");
        if (File.Exists(resources))
            yield return (resources, "resources.pri");
    }
}
