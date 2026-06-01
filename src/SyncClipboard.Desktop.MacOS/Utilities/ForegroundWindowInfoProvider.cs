using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.MacOS.Utilities;

[SupportedOSPlatform("macos")]
internal sealed class ForegroundWindowInfoProvider : IForegroundWindowInfoProvider
{
    private const string AppKitLib = "/System/Library/Frameworks/AppKit.framework/AppKit";

    [DllImport(AppKitLib)]
    private static extern IntPtr NSWorkspaceSharedWorkspace();

    [DllImport(AppKitLib)]
    private static extern IntPtr NSWorkspaceActiveApplication(IntPtr workspace);

    [DllImport(AppKitLib)]
    private static extern IntPtr NSApplicationProcessIdentifier(IntPtr app);

    [DllImport(AppKitLib)]
    private static extern IntPtr NSApplicationLocalizedName(IntPtr app);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr strlen(IntPtr str);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr strlcpy(IntPtr dst, IntPtr src, IntPtr size);

    public ForegroundWindowDetail GetForegroundWindowDetail()
    {
        try
        {
            return ForegroundWindowDetail.Invalid;
        }
        catch
        {
            return ForegroundWindowDetail.Invalid;
        }
    }

    public ForegroundWindowInfo? GetForegroundWindowInfo()
    {
        return null;
    }
}
