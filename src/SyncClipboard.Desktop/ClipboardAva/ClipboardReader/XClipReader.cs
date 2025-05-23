using System.Runtime.Versioning;
using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardReader;

[SupportedOSPlatform("linux")]
public class XClipReader(ILogger logger) : LinuxCmdClipboardReader(logger, "xclip", "xclip", "-selection clipboard -o -t")
{
}
