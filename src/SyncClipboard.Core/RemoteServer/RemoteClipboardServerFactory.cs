using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter;

namespace SyncClipboard.Core.RemoteServer;

public class RemoteClipboardServerFactory : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConfigManager _configManager;
    private readonly AccountManager _accountManager;
    private readonly ILogger _logger;

    private IRemoteClipboardServer? _current;
    private AccountConfig? _currentAccount;
    private IServerAdapter? _currentAdapter;
    private SyncConfig _syncConfig;
    private object? _configDetail;
    private int _disposeState;
    private readonly Lock _serverStateLock = new();

    private sealed record ServerReplacement(IRemoteClipboardServer NewServer, IRemoteClipboardServer? OldServer);

    public event EventHandler? CurrentServerChanged;

    public RemoteClipboardServerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        _accountManager = _serviceProvider.GetRequiredService<AccountManager>();
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _accountManager.CurrentAccountChanged += OnAccountChanged;

        _syncConfig = _configManager.GetConfig<SyncConfig>();
        _configManager.ListenConfig<SyncConfig>(OnSyncConfigChanged);

        ProxyManager.GlobalProxyChanged += OnProxyChanged;
    }

    private void OnProxyChanged()
    {
        ServerReplacement? replacement;
        lock (_serverStateLock)
        {
            if (IsDisposed)
            {
                return;
            }

            // 代理变更与账号切换使用相同的重建流程。
            replacement = ResetCurrentServerCore();
        }
        CompleteServerReplacement(replacement);
    }

    private void OnAccountChanged(AccountConfig accountConfig, object? config)
    {
        ServerReplacement? replacement = null;
        lock (_serverStateLock)
        {
            if (IsDisposed)
            {
                return;
            }

            if (accountConfig.IsEmpty() || config is null)
            {
                replacement = SetEmptyServer(accountConfig);
            }
            else if (_current is EmptyRemoteClipboardServer
                || accountConfig.AccountType != _currentAccount?.AccountType
                || accountConfig.AccountId != _currentAccount?.AccountId)
            {
                replacement = ResetCurrentServerCore(accountConfig, config);
            }
            else if (!Equals(config, _configDetail))
            {
                _configDetail = config;
                _currentAdapter?.SetConfig(_configDetail, _syncConfig);
                _currentAdapter?.SetProxy(ProxyManager.CurrentProxy);
                _current?.OnSyncConfigChanged(_syncConfig);
            }
        }
        CompleteServerReplacement(replacement);
    }

    private void OnSyncConfigChanged(SyncConfig syncConfig)
    {
        lock (_serverStateLock)
        {
            if (IsDisposed)
            {
                return;
            }

            _syncConfig = syncConfig;
            if (_currentAdapter is not null && _configDetail is not null)
            {
                _currentAdapter.SetConfig(_configDetail, _syncConfig);
                _currentAdapter?.SetProxy(ProxyManager.CurrentProxy);
                _current?.OnSyncConfigChanged(_syncConfig);
            }
        }
    }

    public bool HasActiveServer => Current is not EmptyRemoteClipboardServer;

    public IRemoteClipboardServer Current
    {
        get
        {
            ServerReplacement? replacement;
            lock (_serverStateLock)
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                if (_current is not null)
                {
                    return _current;
                }
                replacement = ResetCurrentServerCore();
            }
            CompleteServerReplacement(replacement);
            lock (_serverStateLock)
            {
                // Subscribers may replace the current server or dispose the factory.
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                return _current!;
            }
        }
    }

    public void ResetCurrentServer(AccountConfig? newConfig = null, object? configDetail = null)
    {
        ServerReplacement? replacement;
        lock (_serverStateLock)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            replacement = ResetCurrentServerCore(newConfig, configDetail);
        }
        CompleteServerReplacement(replacement);
    }

    // Called with _serverStateLock held; notification is deferred until after releasing it.
    private ServerReplacement? ResetCurrentServerCore(AccountConfig? newConfig = null, object? configDetail = null)
    {
        var account = newConfig ?? _configManager.GetConfig<AccountConfig>();
        var detail = configDetail ?? _accountManager.GetConfig(account.AccountType, account.AccountId);
        if (account.IsEmpty() || detail is null)
        {
            return SetEmptyServer(account);
        }

        var adapter = CreateAdapter(account.AccountType);
        if (adapter is null)
        {
            return SetEmptyServer(account);
        }

        IRemoteClipboardServer server;
        try
        {
            adapter.SetConfig(detail, _syncConfig);
            adapter.SetProxy(ProxyManager.CurrentProxy);
            if (adapter is IOfficialServerAdapter eventServerAdapter)
            {
                server = new OfficialEventDrivenServer(_serviceProvider, eventServerAdapter);
            }
            else if (adapter is IStorageBasedServerAdapter pollingServerAdapter)
            {
                server = new PollingDrivenServer(_serviceProvider, pollingServerAdapter);
            }
            else
            {
                throw new NotSupportedException("unsupported server type");
            }
        }
        catch
        {
            (adapter as IDisposable)?.Dispose();
            throw;
        }

        try
        {
            server.OnSyncConfigChanged(_syncConfig);
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return ReplaceCurrentServer(server, account, adapter, detail);
        }
        catch
        {
            server.Dispose();
            throw;
        }
    }

    public IServerAdapter? CreateAdapter(string type)
    {
        lock (_serverStateLock)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var adapter = _serviceProvider.GetKeyedService<ServerAdapterFactory>(type)?.Invoke(_serviceProvider);
            if (IsDisposed)
            {
                // A factory can reenter Dispose on this thread while the lock is held.
                (adapter as IDisposable)?.Dispose();
                throw new ObjectDisposedException(nameof(RemoteClipboardServerFactory));
            }
            return adapter;
        }
    }

    private ServerReplacement? SetEmptyServer(AccountConfig account)
    {
        if (_current is EmptyRemoteClipboardServer)
        {
            _currentAccount = account;
            _currentAdapter = null;
            _configDetail = null;
            return null;
        }

        return ReplaceCurrentServer(EmptyRemoteClipboardServer.Instance, account, null, null);
    }

    private ServerReplacement ReplaceCurrentServer(
        IRemoteClipboardServer server,
        AccountConfig account,
        IServerAdapter? adapter,
        object? configDetail)
    {
        var oldServer = _current;
        _current = server;
        _currentAccount = account;
        _currentAdapter = adapter;
        _configDetail = configDetail;
        return new ServerReplacement(server, oldServer);
    }

    private void CompleteServerReplacement(ServerReplacement? replacement)
    {
        if (replacement is null)
        {
            return;
        }

        try
        {
            NotifyCurrentServerChanged();
        }
        finally
        {
            if (!ReferenceEquals(replacement.OldServer, replacement.NewServer))
            {
                try
                {
                    replacement.OldServer?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Write("RemoteServer", $"Failed to dispose the previous server: {ex}");
                }
            }
        }
    }

    private void NotifyCurrentServerChanged()
    {
        var handlers = CurrentServerChanged;
        if (handlers is null || IsDisposed)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            if (IsDisposed)
            {
                break;
            }

            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                var subscriber = handler.Method.DeclaringType?.FullName ?? "Unknown subscriber";
                _logger.Write(
                    "RemoteServer",
                    $"CurrentServerChanged subscriber {subscriber}.{handler.Method.Name} failed: {ex}");
            }
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

    public void Dispose()
    {
        IRemoteClipboardServer? current;
        lock (_serverStateLock)
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            {
                return;
            }

            _accountManager.CurrentAccountChanged -= OnAccountChanged;
            ProxyManager.GlobalProxyChanged -= OnProxyChanged;

            current = _current;
            _current = null;
            _currentAdapter = null;
            _currentAccount = null;
            _configDetail = null;
            CurrentServerChanged = null;
        }

        current?.Dispose();
        GC.SuppressFinalize(this);
    }
}
