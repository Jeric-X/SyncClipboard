using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.MacOS.Utilities;

[SupportedOSPlatform("macos")]
internal sealed class ClipboardOwnerProvider(
    ILogger logger,
    INativeWindowController foregroundWindowInfoProvider) : IClipboardOwnerProvider
{
    private readonly ILogger _logger = logger;
    private readonly INativeWindowController _foregroundWindowInfoProvider = foregroundWindowInfoProvider;
    private const string Tag = "ClipboardOwner";

    public WindowInfo? GetClipboardOwner()
    {
        // macOS doesn't provide API to get clipboard owner window
        // Fallback to foreground window
        _logger.Write(Tag, "macOS doesn't support clipboard owner, using foreground window");
        return _foregroundWindowInfoProvider.GetForegroundWindowInfo();
    }
}
