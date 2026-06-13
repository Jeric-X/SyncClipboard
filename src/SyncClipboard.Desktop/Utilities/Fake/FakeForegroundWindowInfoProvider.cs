using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.Utilities.Fake;

internal sealed class FakeForegroundWindowInfoProvider : IForegroundWindowInfoProvider
{
    public ForegroundWindowDetail? GetForegroundWindowDetail() => null;
    public ForegroundWindowInfo? GetForegroundWindowInfo() => null;
}
