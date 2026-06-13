using System;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.MacOS.Utilities;

/// <summary>
/// A wrapper for CoreFoundation objects that implements IDisposable.
/// Automatically calls CFRelease when disposed.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class CFHandle(IntPtr handle) : IDisposable
{
    private IntPtr _handle = handle;
    private bool _disposed;

    public IntPtr Handle => _handle;
    public bool IsInvalid => _handle == IntPtr.Zero;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_handle != IntPtr.Zero)
        {
            MacInterop.CFRelease(_handle);
            _handle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~CFHandle()
    {
        Dispose();
    }

    /// <summary>
    /// Creates a CFHandle that doesn't release the handle on dispose.
    /// Used for handles that are managed elsewhere (e.g., by NSString).
    /// </summary>
    public static CFHandle CreateManaged(IntPtr handle)
    {
        return new CFHandle(handle) { _disposed = true };
    }
}
