using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Core.RemoteServer.Adapter.Default;
using System.Diagnostics.CodeAnalysis;

namespace SyncClipboard.Core.RemoteServer;

public class RemoteClipboardServerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly ConfigManager _configManager;
    private readonly ITrayIcon _trayIcon;
    private readonly AccountManager _accountManager;

    private IRemoteClipboardServer? _current;
    private AccountConfig? _currentAccount;
    private IServerAdapter? _currentAdapter;
    private SyncConfig _syncConfig;
    private object? _configDetail;

    public event EventHandler? CurrentServerChanged;

    public RemoteClipboardServerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        _trayIcon = _serviceProvider.GetRequiredService<ITrayIcon>();
        _accountManager = _serviceProvider.GetRequiredService<AccountManager>();
        _accountManager.CurrentAccountChanged += OnAccountChanged;

        _syncConfig = _configManager.GetConfig<SyncConfig>();
        _configManager.ListenConfig<SyncConfig>(OnSyncConfigChanged);
    }

    private void OnAccountChanged(AccountConfig accountConfig, object? config)
    {
        if (config is null)
        {
            DisposeExistServer();
            _currentAccount = accountConfig;
            _configDetail = config;
            return;
        }

        if (accountConfig.AccountType != _currentAccount?.AccountType || accountConfig.AccountId != _currentAccount?.AccountId)
        {
            ResetCurrentServer(accountConfig, config);
        }
        else if (Equals(config, _configDetail) == false)
        {
            _configDetail = config;
            _currentAdapter?.SetConfig(_configDetail, _syncConfig);
            _current?.OnSyncConfigChanged(_syncConfig);
        }
    }

    private void OnSyncConfigChanged(SyncConfig syncConfig)
    {
        _syncConfig = syncConfig;
        if (_currentAdapter is not null && _configDetail is not null)
        {
            _currentAdapter.SetConfig(_configDetail, _syncConfig);
            _current?.OnSyncConfigChanged(_syncConfig);
        }
    }

    public IRemoteClipboardServer Current
    {
        get
        {
            if (_current is null)
                ResetCurrentServer();
            return _current;
        }
    }

    [MemberNotNull(nameof(_current))]
    public void ResetCurrentServer(AccountConfig? newConfig = null, object? configDetail = null)
    {
        DisposeExistServer();
        _currentAccount = newConfig ?? _configManager.GetConfig<AccountConfig>();
        _currentAdapter = GetAdapter(_currentAccount.AccountType);

        _currentAdapter ??= new DefaultStorageAdapter();
        _configDetail = configDetail ?? _accountManager.GetConfig(_currentAccount.AccountType, _currentAccount.AccountId);
        if (_configDetail is not null)
        {
            _currentAdapter.SetConfig(_configDetail, _syncConfig);
        }

        if (_currentAdapter is IOfficialServerAdapter eventServerAdapter)
        {
            _current = new OfficialEventDrivenServer(_serviceProvider, eventServerAdapter);
        }
        else if (_currentAdapter is IStorageBasedServerAdapter pollingServerAdapter)
        {
            _current = new PollingDrivenServer(_serviceProvider, pollingServerAdapter);
        }
        else
        {
            throw new NotSupportedException("unsupported server type");
        }
        _current.OnSyncConfigChanged(_syncConfig);
        CurrentServerChanged?.Invoke(this, EventArgs.Empty);
    }

    public IServerAdapter GetAdapter(string type)
    {
        var adapter = _serviceProvider.GetKeyedService<IServerAdapter>(type);
        adapter ??= new DefaultStorageAdapter();
        return adapter;
    }

    public void DisposeExistServer()
    {
        var oldServer = _current;
        oldServer?.Dispose();
        _current = null;
    }
}