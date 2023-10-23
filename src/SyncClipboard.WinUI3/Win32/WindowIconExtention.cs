using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;

namespace SyncClipboard.WinUI3.Win32;

// From https://github.com/microsoft/microsoft-ui-xaml/issues/8134#issuecomment-1429388672
public static class WindowIconExtention
{
    [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, UInt32 uType, int cxDesired, int cyDesired, UInt32 fuLoad);

    private const int IMAGE_ICON = 1;

    private const int LR_LOADFROMFILE = 0x00000010;

    private const int ICON_BIG = 1;

    [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public const int WM_SETICON = 0x0080;

    public static void SetWindowIcon(this Window window, string path)
    {
        IntPtr icon = LoadImage(IntPtr.Zero, path, IMAGE_ICON, 64, 64, LR_LOADFROMFILE);
        IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _ = SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_BIG, icon);
    }
}
