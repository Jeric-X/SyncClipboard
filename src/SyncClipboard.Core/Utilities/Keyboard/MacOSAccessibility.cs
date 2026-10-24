using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SyncClipboard.Core.Utilities.Keyboard;

internal static class MacOSAccessibility
{
    private const string ApplicationServicesLibrary = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    internal static void OpenSettings()
    {
        using var process = Process.Start(new ProcessStartInfo(
            "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")
        {
            UseShellExecute = true
        });
    }

    [DllImport(ApplicationServicesLibrary)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool AXIsProcessTrusted();

    [DllImport(ApplicationServicesLibrary)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrustedWithOptions(nint options);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool CGPreflightPostEventAccess();

    [DllImport(ApplicationServicesLibrary)]
    private static extern nint CFDictionaryCreate(nint allocator, ref nint keys, ref nint values,
        nint count, nint keyCallbacks, nint valueCallbacks);

    [DllImport(ApplicationServicesLibrary)]
    private static extern void CFRelease(nint value);

    internal static async Task ResetAsync()
    {
        // Scope the reset to this app; omitting the bundle ID would reset every app's authorization.
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/tccutil")
        {
            ArgumentList = { "reset", "Accessibility", "xyz.jericx.desktop.syncclipboard" },
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        }) ?? throw new InvalidOperationException("Failed to start accessibility permission reset.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(error, output);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill();
            await process.WaitForExitAsync(CancellationToken.None);
            throw new TimeoutException("Accessibility permission reset timed out.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Accessibility permission reset failed ({process.ExitCode}): {await error}");
        }
    }

    internal static void Request()
    {
        var library = NativeLibrary.Load(ApplicationServicesLibrary);
        nint options = 0;
        try
        {
            var promptKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kAXTrustedCheckOptionPrompt"));
            var trueValue = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kCFBooleanTrue"));
            var keyCallbacks = NativeLibrary.GetExport(library, "kCFTypeDictionaryKeyCallBacks");
            var valueCallbacks = NativeLibrary.GetExport(library, "kCFTypeDictionaryValueCallBacks");
            options = CFDictionaryCreate(0, ref promptKey, ref trueValue, 1, keyCallbacks, valueCallbacks);
            if (options == 0)
                throw new OutOfMemoryException();
            AXIsProcessTrustedWithOptions(options);
        }
        finally
        {
            if (options != 0)
                CFRelease(options);
            NativeLibrary.Free(library);
        }
    }
}
