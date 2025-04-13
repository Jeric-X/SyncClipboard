using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;

namespace SyncClipboard.WinUI3.Win32;

// From https://github.com/microsoft/microsoft-ui-xaml/issues/8134#issuecomment-1429388672
public static partial class WindowIconExtention
{
    [LibraryImport("User32.dll", EntryPoint = "LoadImageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadImage(nint hInst, string lpszName, UInt32 uType, int cxDesired, int cyDesired, UInt32 fuLoad);

    private const int IMAGE_ICON = 1;

    private const int LR_LOADFROMFILE = 0x00000010;

    private const int ICON_BIG = 1;

    [LibraryImport("User32.dll", EntryPoint = "SendMessageW", SetLastError = true)]
    private static partial int SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    public const int WM_SETICON = 0x0080;

    public static void SetWindowIcon(this Window window, string path)
    {
        IntPtr icon = LoadImage(IntPtr.Zero, path, IMAGE_ICON, 64, 64, LR_LOADFROMFILE);
        IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _ = SendMessage(hWnd, WM_SETICON, ICON_BIG, icon);
    }
}
