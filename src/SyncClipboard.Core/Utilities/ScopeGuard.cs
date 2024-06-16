namespace SyncClipboard.Core.Utilities;

public sealed class ScopeGuard : IDisposable
{
    private readonly Action _action;
    public ScopeGuard(Action action)
    {
        _action = action;
    }

    public void Dispose()
    {
        _action?.Invoke();
    }
}
