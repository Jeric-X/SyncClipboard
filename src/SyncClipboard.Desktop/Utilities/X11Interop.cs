using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.Utilities;

[SupportedOSPlatform("linux")]
internal static class X11Interop
{
    private const string LibX11 = "libX11.so.6";
    private static bool? _isAvailable;

    public static bool IsAvailable
    {
        get
        {
            _isAvailable ??= NativeLibrary.TryLoad(LibX11, out _);
            return _isAvailable.Value;
        }
    }

    [DllImport(LibX11)]
    internal static extern nint XOpenDisplay(nint display);

    [DllImport(LibX11)]
    internal static extern int XCloseDisplay(nint display);

    [DllImport(LibX11)]
    internal static extern nint XDefaultRootWindow(nint display);

    [DllImport(LibX11, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    internal static extern nint XInternAtom(nint display, string atomName, bool onlyIfExists);

    [DllImport(LibX11)]
    internal static extern nint XGetSelectionOwner(nint display, nint selection);

    [DllImport(LibX11)]
    internal static extern int XGetWindowProperty(
        nint display,
        nint window,
        nint property,
        long longOffset,
        long longLength,
        bool delete,
        nint reqType,
        out nint actualTypeReturn,
        out int actualFormatReturn,
        out long nItemsReturn,
        out long bytesAfterReturn,
        out nint propReturn);

    [DllImport(LibX11)]
    internal static extern int XFree(nint data);

    [DllImport(LibX11)]
    internal static extern int XGetWindowAttributes(
        nint display,
        nint window,
        out XWindowAttributes attributes);

    [DllImport(LibX11, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    internal static extern int XFetchName(
        nint display,
        nint window,
        out nint windowName);

    [DllImport(LibX11)]
    internal static extern int XGetWMName(
        nint display,
        nint window,
        out nint windowName);

    [DllImport(LibX11)]
    internal static extern uint XGetInputFocus(
        nint display,
        out nint focusWindow,
        out int revertTo);

    [DllImport(LibX11)]
    internal static extern int XQueryTree(
        nint display,
        nint window,
        out nint rootReturn,
        out nint parentReturn,
        out nint childrenReturn,
        out int nChildrenReturn);

    [DllImport(LibX11)]
    internal static extern int XGetGeometry(
        nint display,
        nint drawable,
        out nint rootReturn,
        out int xReturn,
        out int yReturn,
        out int widthReturn,
        out int heightReturn,
        out int borderWidthReturn,
        out int depthReturn);

    [DllImport(LibX11)]
    internal static extern int XQueryPointer(
        nint display,
        nint window,
        out nint root_return,
        out nint child_return,
        out int root_x_return,
        out int root_y_return,
        out int win_x_return,
        out int win_y_return,
        out uint mask_return);
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
    internal nint visual;
    internal nint root;
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
    internal nint screen;
}
