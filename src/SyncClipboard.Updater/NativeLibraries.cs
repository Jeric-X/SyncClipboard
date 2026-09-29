using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;

namespace SyncClipboard.Updater;

internal static class NativeLibraries
{
    public static void Register()
    {
        NativeLibrary.SetDllImportResolver(typeof(SkiaSharp.SKBitmap).Assembly, Resolve);
        NativeLibrary.SetDllImportResolver(typeof(HarfBuzzSharp.Blob).Assembly, Resolve);
        NativeLibrary.SetDllImportResolver(typeof(AvaloniaNativePlatformOptions).Assembly, Resolve);
        NativeLibrary.SetDllImportResolver(typeof(Win32PlatformOptions).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        var filename = name.EndsWith(extension, StringComparison.Ordinal) ? name : name + extension;
        var path = Path.Combine(AppContext.BaseDirectory, filename);
        if (!File.Exists(path)) return IntPtr.Zero;
        var handle = NativeLibrary.Load(path);
        Console.WriteLine("NATIVE_LIBRARY=" + Path.GetFileName(path));
        return handle;
    }
}
