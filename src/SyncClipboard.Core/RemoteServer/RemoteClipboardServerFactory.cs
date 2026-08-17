using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter;
using System.Diagnostics.CodeAnalysis;

namespace SyncClipboard.Core.RemoteServer;

public class RemoteClipboardServerFactory
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
        // 不调 SetProxy+ApplyConfig 的轻量重建，直接走 ResetCurrentServer 与账号切换语义一致。
        ResetCurrentServer();
    }

    private void OnAccountChanged(AccountConfig accountConfig, object? config)
    {
        if (accountConfig.IsEmpty() || config is null)
        {
            SetEmptyServer(accountConfig);
            return;
        }

        if (_current is EmptyRemoteClipboardServer
            || accountConfig.AccountType != _currentAccount?.AccountType
            || accountConfig.AccountId != _currentAccount?.AccountId)
        {
            ResetCurrentServer(accountConfig, config);
        }
        else if (!Equals(config, _configDetail))
        {
            _configDetail = config;
            _currentAdapter?.SetConfig(_configDetail, _syncConfig);
            _currentAdapter?.SetProxy(ProxyManager.CurrentProxy);
            _current?.OnSyncConfigChanged(_syncConfig);
        }
    }

    private void OnSyncConfigChanged(SyncConfig syncConfig)
    {
        _syncConfig = syncConfig;
        if (_currentAdapter is not null && _configDetail is not null)
        {
            _currentAdapter.SetConfig(_configDetail, _syncConfig);
            _currentAdapter.SetProxy(ProxyManager.CurrentProxy); // ApplyConfig 之前 push 最新代理
            _current?.OnSyncConfigChanged(_syncConfig);
        }
    }

    public bool HasActiveServer => Current is not EmptyRemoteClipboardServer;

    public IRemoteClipboardServer Current
    {
        get
        {
            if (_current is null)
            {
                ResetCurrentServer();
            }

            return _current;
        }
    }

    [MemberNotNull(nameof(_current))]
    public void ResetCurrentServer(AccountConfig? newConfig = null, object? configDetail = null)
    {
        var account = newConfig ?? _configManager.GetConfig<AccountConfig>();
        var detail = configDetail ?? _accountManager.GetConfig(account.AccountType, account.AccountId);
        if (account.IsEmpty() || detail is null)
        {
            SetEmptyServer(account);
            return;
        }

        var adapter = GetAdapter(account.AccountType);
        if (adapter is null)
        {
            SetEmptyServer(account);
            return;
        }

        adapter.SetConfig(detail, _syncConfig);
        adapter.SetProxy(ProxyManager.CurrentProxy);
        IRemoteClipboardServer server;
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

        server.OnSyncConfigChanged(_syncConfig);
        ReplaceCurrentServer(server, account, adapter, detail);
    }

    public IServerAdapter? GetAdapter(string type) =>
        _serviceProvider.GetKeyedService<IServerAdapter>(type);

    [MemberNotNull(nameof(_current))]
    private void SetEmptyServer(AccountConfig account)
    {
        if (_current is EmptyRemoteClipboardServer)
        {
            _currentAccount = account;
            _currentAdapter = null;
            _configDetail = null;
            return;
        }

        ReplaceCurrentServer(EmptyRemoteClipboardServer.Instance, account, null, null);
    }

    [MemberNotNull(nameof(_current))]
    private void ReplaceCurrentServer(
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

        try
        {
            NotifyCurrentServerChanged();
        }
        finally
        {
            if (!ReferenceEquals(oldServer, server))
            {
                try
                {
                    oldServer?.Dispose();
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
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
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
}
