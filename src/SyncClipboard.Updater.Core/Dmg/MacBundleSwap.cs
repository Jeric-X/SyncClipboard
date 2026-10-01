using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SyncClipboard.Updater.Dmg;

internal static class MacBundleSwap
{
    public static void Replace(string prepared, string target)
    {
        if (!Directory.Exists(target))
        {
            Directory.Move(prepared, target);
            return;
        }
        if (RenameSwap(prepared, target, 2) == 0)
            return;
        HandleSwapFailure(prepared, target, Marshal.GetLastPInvokeError());
    }

    internal static void HandleSwapFailure(string prepared, string target, int error)
    {
        // ENOTSUP / EOPNOTSUPP: only unsupported filesystems use the delete-and-rename fallback.
        if (error is not (45 or 102))
            throw new IOException(new Win32Exception(error).Message + ": " + target);
        Directory.Delete(target, true);
        Directory.Move(prepared, target);
    }

    public static int RunCommand(string prepared, string target)
    {
        try
        {
            Replace(prepared, target);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "renamex_np", SetLastError = true)]
    private static extern int RenameSwap([MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string target, uint flags);
}
