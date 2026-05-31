using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IForegroundWindowInfoProvider
{
    ForegroundWindowInfo GetForegroundWindowInfo();
}
