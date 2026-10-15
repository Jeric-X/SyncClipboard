using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;

namespace SyncClipboard.Core.Utilities.History;

public sealed class HistorySyncSession : IDisposable
{
    private readonly RemoteClipboardServerFactory _serverFactory;
    private readonly ConfigBase _runtimeConfig;
    private readonly Lock _stateLock = new();
    private CancellationTokenSource _source = new();
    private bool _enabled;
    private bool _disposed;

    public HistorySyncSession(RemoteClipboardServerFactory serverFactory,
        [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig)
    {
        _serverFactory = serverFactory;
        _runtimeConfig = runtimeConfig;
        _source.Cancel();
        _serverFactory.CurrentServerChanged += OnServerChanged;
        _runtimeConfig.ConfigChanged += OnConfigChanged;
        OnConfigChanged();
    }

    public CancellationToken Token
    {
        get
        {
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _source.Token;
            }
        }
    }

    private void OnServerChanged(object? sender, EventArgs e) => Reset(force: true);
    private void OnConfigChanged() => Reset(force: false);

    private void Reset(bool force)
    {
        CancellationTokenSource previous;
        lock (_stateLock)
        {
            if (_disposed)
                return;
            var enabled = _runtimeConfig.GetConfig<RuntimeHistoryConfig>().EnableSyncHistory;
            if (!force && enabled == _enabled)
                return;
            _enabled = enabled;
            previous = _source;
            _source = new CancellationTokenSource();
            if (!enabled)
                _source.Cancel();
        }
        CancelAndDispose(previous);
    }

    private static void CancelAndDispose(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        finally
        {
            source.Dispose();
        }
    }

    void IDisposable.Dispose()
    {
        CancellationTokenSource source;
        lock (_stateLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            source = _source;
        }
        _serverFactory.CurrentServerChanged -= OnServerChanged;
        _runtimeConfig.ConfigChanged -= OnConfigChanged;
        CancelAndDispose(source);
        GC.SuppressFinalize(this);
    }
}
