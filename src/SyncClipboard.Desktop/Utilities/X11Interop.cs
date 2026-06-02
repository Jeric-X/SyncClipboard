using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.Utilities;

[SupportedOSPlatform("linux")]
internal static class X11Interop
{
    private const string LibX11 = "libX11.so.6";

    [DllImport(LibX11)]
    internal static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport(LibX11)]
    internal static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    internal static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

    [DllImport(LibX11)]
    internal static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);

    [DllImport(LibX11)]
    internal static extern int XGetWindowProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        long longOffset,
        long longLength,
        bool delete,
        IntPtr reqType,
        out IntPtr actualTypeReturn,
        out int actualFormatReturn,
        out long nItemsReturn,
        out long bytesAfterReturn,
        out IntPtr propReturn);

    [DllImport(LibX11)]
    internal static extern int XFree(IntPtr data);

    [DllImport(LibX11)]
    internal static extern int XGetWindowAttributes(
        IntPtr display,
        IntPtr window,
        out XWindowAttributes attributes);

    [DllImport(LibX11, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    internal static extern int XFetchName(
        IntPtr display,
        IntPtr window,
        out IntPtr windowName);

    [DllImport(LibX11)]
    internal static extern int XGetWMName(
        IntPtr display,
        IntPtr window,
        out IntPtr windowName);

    [DllImport(LibX11)]
    internal static extern uint XGetInputFocus(
        IntPtr display,
        out IntPtr focusWindow,
        out int revertTo);

    [DllImport(LibX11)]
    internal static extern int XQueryTree(
        IntPtr display,
        IntPtr window,
        out IntPtr rootReturn,
        out IntPtr parentReturn,
        out IntPtr childrenReturn,
        out int nChildrenReturn);

    [DllImport(LibX11)]
    internal static extern int XGetGeometry(
        IntPtr display,
        IntPtr drawable,
        out IntPtr rootReturn,
        out int xReturn,
        out int yReturn,
        out int widthReturn,
        out int heightReturn,
        out int borderWidthReturn,
        out int depthReturn);
}

[SupportedOSPlatform("linux")]
internal struct XWindowAttributes
{
    internal int x;
    internal int y;
    internal int width;
    internal int height;
    internal int border_width;
    internal int depth;
    internal IntPtr visual;
    internal IntPtr root;
    internal int c_class;
    internal int bit_gravity;
    internal int win_gravity;
    internal int backing_store;
    internal ulong backing_planes;
    internal ulong backing_pixel;
    internal bool save_under;
    internal ulong colormap;
    internal bool map_installed;
    internal int map_state;
    internal long all_event_masks;
    internal long your_event_mask;
    internal long do_not_propagate_mask;
    internal bool override_redirect;
    internal IntPtr screen;
}
