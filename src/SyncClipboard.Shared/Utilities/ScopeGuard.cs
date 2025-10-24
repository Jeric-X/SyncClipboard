namespace SyncClipboard.Shared.Utilities;

public sealed class ScopeGuard(Action action) : IDisposable
{
    public void Dispose()
    {
        action?.Invoke();
    }
}
