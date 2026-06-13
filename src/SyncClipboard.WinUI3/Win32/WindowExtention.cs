using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.UI;

namespace SyncClipboard.WinUI3.Win32;

public static partial class WindowExtention
{
    [LibraryImport("User32.dll", EntryPoint = "LoadImageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadImage(nint hInst, string lpszName, UInt32 uType, int cxDesired, int cyDesired, UInt32 fuLoad);

    private const int IMAGE_ICON = 1;

    private const int LR_LOADFROMFILE = 0x00000010;

    private const int ICON_BIG = 1;

    [LibraryImport("User32.dll", EntryPoint = "SendMessageW", SetLastError = true)]
    private static partial int SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    public const int WM_SETICON = 0x0080;

    [LibraryImport("User32.dll", EntryPoint = nameof(MonitorFromPoint), SetLastError = true)]
    private static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("Shcore.dll", EntryPoint = nameof(GetDpiForMonitor), SetLastError = true)]
    private static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public static double GetScaleFactorForPoint(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        if (monitor == nint.Zero)
        {
            return 1.0;
        }

        var result = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _);
        if (result != 0)
        {
            return 1.0;
        }

        return dpiX / 96.0;
    }

    public static int PhysicalToDip(int physicalPixel, int x, int y)
    {
        return (int)Math.Round(physicalPixel / GetScaleFactorForPoint(x, y));
    }

    public static int DipToPhysical(int dip, int x, int y)
    {
        return (int)Math.Round(dip * GetScaleFactorForPoint(x, y));
    }

    public static (int Width, int Height) PhysicalToDip(int width, int height, int x, int y)
    {
        var scale = GetScaleFactorForPoint(x, y);
        return ((int)Math.Round(width / scale), (int)Math.Round(height / scale));
    }

    public static (int Width, int Height) DipToPhysical(int width, int height, int x, int y)
    {
        var scale = GetScaleFactorForPoint(x, y);
        return ((int)Math.Round(width * scale), (int)Math.Round(height * scale));
    }

    public static (int Width, int Height) PhysicalToDip(int width, int height, double scale)
    {
        return ((int)Math.Round(width / scale), (int)Math.Round(height / scale));
    }

    public static (int Width, int Height) DipToPhysical(int width, int height, double scale)
    {
        return ((int)Math.Round(width * scale), (int)Math.Round(height * scale));
    }

    public static void ResizeDip(this Window window, int dipWidth, int dipHeight, int x, int y)
    {
        var (width, height) = DipToPhysical(dipWidth, dipHeight, x, y);
        window.AppWindow.Resize(new SizeInt32(width, height));
    }

    public static void ResizeDip(this Window window, int dipWidth, int dipHeight)
    {
        var primaryDisplay = DisplayArea.Primary;
        var (x, y) = primaryDisplay != null
            ? (primaryDisplay.WorkArea.X + (primaryDisplay.WorkArea.Width / 2), primaryDisplay.WorkArea.Y + (primaryDisplay.WorkArea.Height / 2))
            : (0, 0);
        window.ResizeDip(dipWidth, dipHeight, x, y);
    }

    public static void CenterOnScreenDip(this Window window, int dipWidth, int dipHeight)
    {
        var primaryDisplay = DisplayArea.Primary;
        if (primaryDisplay == null)
        {
            window.ResizeDip(dipWidth, dipHeight, 0, 0);
            return;
        }

        var workArea = primaryDisplay.WorkArea;
        var (width, height) = DipToPhysical(dipWidth, dipHeight, workArea.X + (workArea.Width / 2), workArea.Y + (workArea.Height / 2));
        var x = workArea.X + ((workArea.Width - width) / 2);
        var y = workArea.Y + ((workArea.Height - height) / 2);

        window.AppWindow.Move(new PointInt32(x, y));
        window.AppWindow.Resize(new SizeInt32(width, height));
    }

    public static void SetWindowIcon(this Window window, string path)
    {
        IntPtr icon = LoadImage(IntPtr.Zero, path, IMAGE_ICON, 64, 64, LR_LOADFROMFILE);
        IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _ = SendMessage(hWnd, WM_SETICON, ICON_BIG, icon);
    }

    public static void SetTitleBarButtonForegroundColor(this Window window)
    {
        void ChangeTitleBarButtonForegroundColor(FrameworkElement sender, object? _)
        {
            var actualTheme = sender.ActualTheme.ToString();
            var themeResource = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[actualTheme];
            window.AppWindow.TitleBar.ButtonForegroundColor = (Color)themeResource["TitleBarButtonForegroundColor"];
        }

        ChangeTitleBarButtonForegroundColor((FrameworkElement)window.Content, null);
        ((FrameworkElement)window.Content).ActualThemeChanged += ChangeTitleBarButtonForegroundColor;
    }

    public static void SetTheme(this Window window, string theme)
    {
        ((FrameworkElement)window.Content).RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }
}
