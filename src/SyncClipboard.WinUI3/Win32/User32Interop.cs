using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace SyncClipboard.WinUI3.Win32;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GUITHREADINFO
{
    public int cbSize;
    public int flags;
    public IntPtr hwndActive;
    public IntPtr hwndFocus;
    public IntPtr hwndCapture;
    public IntPtr hwndMenuOwner;
    public IntPtr hwndMoveSize;
    public IntPtr hwndCaret;
    public Rectangle rcCaret;
}

[ComImport]
[Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IAccessible
{
#pragma warning disable IDE1006
    object accParent { get; }
    int accChildCount { get; }
    object accName(object varChild);
    object accValue(object varChild);
    object accDescription(object varChild);
    object accRole(object varChild);
    object accState(object varChild);
    object accHelp(object varChild);
    object accHelpTopic(out string pszHelpFile, object varChild);
    object accKeyboardShortcut(object varChild);
    object accFocus { get; }
    object accSelection { get; }
    object accLocation(out int pxLeft, out int pyTop, out int pcxWidth, out int pcyHeight, object varChild);
    object accNavigate(int navDir, object varStart);
    object accHitTest(int xLeft, int yTop);
    void accDoDefaultAction(object varChild);
#pragma warning restore IDE1006
}

internal static class User32Interop
{
    internal const int OBJID_CARET = -8;
    internal static readonly Guid IID_IAccessible = new(0x618736E0, 0x3C3D, 0x11CF, 0x81, 0x0C, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, [Out] StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(int idThread, ref GUITHREADINFO pgui);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

    [DllImport("oleacc.dll")]
    internal static extern int AccessibleObjectFromWindow(IntPtr hwnd, int dwObjectID, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetClipboardOwner();
}
