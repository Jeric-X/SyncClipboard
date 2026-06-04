using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System.Runtime.Versioning;

namespace SyncClipboard.Desktop.Utilities;

[SupportedOSPlatform("linux")]
internal sealed class CaretPositionProvider(ILogger logger) : ICaretPositionProvider
{
    private readonly ILogger _logger = logger;
    private const string Tag = "CaretPosition";

    public ScreenPosition? GetCaretPosition()
    {
        return null;
    }
}
