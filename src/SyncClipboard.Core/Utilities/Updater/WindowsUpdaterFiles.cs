namespace SyncClipboard.Core.Utilities.Updater;

internal static class WindowsUpdaterFiles
{
    // Reuse native UI dependencies, never the main application's managed assemblies or CLR.
    // Framework-dependent WinUI builds use the installed SDK through the copied bootstrap DLL.
    private static readonly string[] FileNames =
    [
        // Debug PRI files reference App.xbf on disk; Release normally embeds it.
        "SyncClipboard.Updater.exe", "SyncClipboard.pri", "App.xbf",
        "CoreMessagingXP.dll", "dcompi.dll", "dwmcorei.dll", "DwmSceneI.dll", "DWriteCore.dll", "marshal.dll",
        "Microsoft.DirectManipulation.dll", "Microsoft.InputStateManager.dll", "Microsoft.Internal.FrameworkUdk.dll",
        "Microsoft.UI.Composition.OSSupport.dll", "Microsoft.UI.Input.dll", "Microsoft.UI.Windowing.Core.dll",
        "Microsoft.UI.Windowing.dll", "Microsoft.UI.Xaml.Controls.dll", "Microsoft.UI.Xaml.dll",
        "Microsoft.UI.Xaml.Internal.dll", "Microsoft.UI.Xaml.resources.19h1.dll", "Microsoft.UI.Xaml.resources.common.dll",
        "Microsoft.Windows.ApplicationModel.Resources.dll", "Microsoft.WindowsAppRuntime.dll",
        "Microsoft.WindowsAppRuntime.Bootstrap.dll", "MRM.dll", "wuceffectsi.dll",
        "libSkiaSharp.dll", "libHarfBuzzSharp.dll"
    ];

    internal static IEnumerable<(string Source, string Name)> GetFiles(string directory)
    {
        foreach (var name in FileNames)
        {
            var path = Path.Combine(directory, name);
            // The executable is required; copying it must fail if it is missing.
            if (name != "SyncClipboard.Updater.exe" && !File.Exists(path))
                continue;
            // WinUI resolves the main application's merged theme resources under this name.
            yield return (path, name == "SyncClipboard.pri" ? "resources.pri" : name);
        }
    }
}
