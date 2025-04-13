namespace SyncClipboard.Core.Utilities;

public sealed class ScopeGuard(Action action) : IDisposable
{
    public void Dispose()
    {
        action?.Invoke();
    }
}
