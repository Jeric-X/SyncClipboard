using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using System.Diagnostics.CodeAnalysis;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Core.RemoteServer.Adapter.Default;

namespace SyncClipboard.Core.RemoteServer;

public class RemoteClipboardServerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly ConfigManager _configManager;
    private readonly IAppConfig _appConfig;
    private readonly ITrayIcon _trayIcon;
    private readonly AccountManager _accountManager;

    private IRemoteClipboardServer? _current;
    private AccountConfig? _currentAccount;
    private IStorageOnlyServerAdapter? _currentAdapter;

    public RemoteClipboardServerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        _appConfig = _serviceProvider.GetRequiredService<IAppConfig>();
        _trayIcon = _serviceProvider.GetRequiredService<ITrayIcon>();
        _accountManager = _serviceProvider.GetRequiredService<AccountManager>();
        _accountManager.CurrentAccountChanged += OnAccountChanged;
    }

    private void OnAccountChanged(string type, string? accountId, object? config)
    {
        if (_current is null || config is null)
        {
            return;
        }

        if (type != _currentAccount?.AccountType)
        {
            ResetCurrentServer();
            return;
        }

        _currentAdapter?.OnConfigChanged(config);
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
    public void ResetCurrentServer(AccountConfig? newConfig = null)
    {
        DisposeExistServer();
        _currentAccount = newConfig ?? _configManager.GetConfig<AccountConfig>();
        _currentAdapter = _serviceProvider.GetKeyedService<IStorageOnlyServerAdapter>(_currentAccount.AccountType);

        _currentAdapter ??= new DefaultStorageAdapter();
        _current = new PollingDrivenServer(_logger, _configManager, _appConfig, _trayIcon, _currentAdapter);
    }

    public void DisposeExistServer()
    {
        var oldServer = _current;
        oldServer?.Dispose();
        _current = null;
    }
}