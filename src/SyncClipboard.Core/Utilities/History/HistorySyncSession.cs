using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;

namespace SyncClipboard.Core.Utilities.History;

public sealed class HistorySyncSession : IDisposable
{
    private readonly RemoteClipboardServerFactory _serverFactory;
    private readonly ConfigBase _runtimeConfig;
    private readonly Lock _stateLock = new();
    private CancellationTokenSource _accountCts = new();
    private CancellationTokenSource _automaticCts = new();
    private Context? _current;
    private bool _automaticEnabled;
    private bool _stopped;
    private bool _disposed;

    public HistorySyncSession(RemoteClipboardServerFactory serverFactory,
        [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig)
    {
        _serverFactory = serverFactory;
        _runtimeConfig = runtimeConfig;
        _serverFactory.CurrentServerChanged += OnServerChanged;
        _runtimeConfig.ConfigChanged += Refresh;
    }

    public Context Capture()
    {
        Refresh();
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _current!;
        }
    }

    internal void Start() => Refresh(false);
    internal void Stop() => Refresh(true);
    private void OnServerChanged(object? sender, EventArgs e) => Refresh();
    private void Refresh() => Refresh(null);

    private void Refresh(bool? stopped)
    {
        CancellationTokenSource? oldAccount = null;
        CancellationTokenSource? oldAutomatic;
        lock (_stateLock)
        {
            if (_disposed)
                return;
            if (stopped.HasValue)
                _stopped = stopped.Value;
            var server = _serverFactory.Current as IOfficialSyncServer;
            var enabled = !_stopped && server is not null &&
                _runtimeConfig.GetConfig<RuntimeHistoryConfig>().EnableSyncHistory;
            var accountChanged = _current is null || !ReferenceEquals(server, _current.Server);
            if (!accountChanged && enabled == _automaticEnabled)
                return;

            if (accountChanged)
            {
                oldAccount = _accountCts;
                _accountCts = new CancellationTokenSource();
            }
            oldAutomatic = _automaticCts;
            _automaticCts = CancellationTokenSource.CreateLinkedTokenSource(_accountCts.Token);
            if (!enabled)
                _automaticCts.Cancel();
            _automaticEnabled = enabled;
            _current = new Context(this, server, _accountCts.Token, _automaticCts.Token);
        }
        // Cancellation callbacks may call back into the queue; do not invoke them under the session lock.
        try
        {
            CancelAndDispose(oldAccount);
        }
        finally
        {
            CancelAndDispose(oldAutomatic);
        }
    }

    private static void CancelAndDispose(CancellationTokenSource? source)
    {
        if (source is null)
            return;
        try
        {
            source.Cancel();
        }
        finally
        {
            source.Dispose();
        }
    }

    private void Check(Context context, bool automatic)
    {
        Refresh();
        lock (_stateLock)
        {
            if (_disposed || _current is null || context.AccountToken != _current.AccountToken ||
                (automatic && (!_automaticEnabled || context.AutomaticToken != _current.AutomaticToken)))
                throw new OperationCanceledException(automatic ? context.AutomaticToken : context.AccountToken);
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _serverFactory.CurrentServerChanged -= OnServerChanged;
        _runtimeConfig.ConfigChanged -= Refresh;
        try
        {
            CancelAndDispose(_accountCts);
        }
        finally
        {
            CancelAndDispose(_automaticCts);
        }
    }

    public sealed class Context
    {
        private readonly HistorySyncSession _owner;
        public IOfficialSyncServer? Server { get; }
        public CancellationToken AccountToken { get; }
        public CancellationToken AutomaticToken { get; }

        internal Context(HistorySyncSession owner, IOfficialSyncServer? server,
            CancellationToken accountToken, CancellationToken automaticToken)
        {
            _owner = owner;
            Server = server;
            AccountToken = accountToken;
            AutomaticToken = automaticToken;
        }

        public void ThrowIfCancellationRequested(bool automatic = true)
        {
            (automatic ? AutomaticToken : AccountToken).ThrowIfCancellationRequested();
            _owner.Check(this, automatic);
        }

        public CancellationTokenSource? Link(CancellationToken token, bool automatic = true)
        {
            ThrowIfCancellationRequested(automatic);
            return token.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(token, automatic ? AutomaticToken : AccountToken)
                : null;
        }
    }
}
