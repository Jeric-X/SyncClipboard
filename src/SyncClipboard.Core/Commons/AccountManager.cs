using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SyncClipboard.Core.Commons;

public class AccountManager
{
    public enum AccountSelectionOrigin
    {
        Automatic,
        Manual,
        External,
        Deleted,
    }

    public sealed record AccountSelectionChangedEventArgs(AccountConfig Account, AccountSelectionOrigin Origin);

    public delegate void AccountChangedHandler(AccountConfig accountConfig, object? config);
    public event AccountChangedHandler? CurrentAccountChanged;

    public event EventHandler<AccountSelectionChangedEventArgs>? AccountSelectionChanged;

    public delegate void SavedAccountsChangedHandler(IEnumerable<DisplayedAccountConfig> newAccounts);
    public event SavedAccountsChangedHandler? SavedAccountsChanged;

    private AccountConfig? _accountConfig;
    private object? _currentAccount;
    private List<DisplayedAccountConfig> _lastSavedAccounts = [];

    private readonly IReadOnlyList<AdapterConfigRegistration> _registeredConfigTypes =
        AccountConfigRegistry.Configurations;
    private readonly ConfigManager _configManager;
    private readonly ILogger _logger;

    public AccountManager(ConfigManager configManager, ILogger logger)
    {
        _configManager = configManager;
        _logger = logger;

        OnAccountConfigChanged();
        _configManager.ConfigChanged += OnAccountConfigChanged;
    }

    public Type? GetRegisteredType(string typeName)
    {
        return _registeredConfigTypes
            .FirstOrDefault(config => config.TypeName == typeName)?
            .ConfigType;
    }

    public object? GetConfig(string type, string accountId)
    {
        var accountsNode = _configManager.GetNode(AccountConfig.SavedAccountsConfigKey);
        if (accountsNode is null)
            return null;

        var typeAccounts = accountsNode[type];
        if (typeAccounts is null)
            return null;

        var accountNode = typeAccounts[accountId];
        if (accountNode is null)
            return null;

        var registeredType = GetRegisteredType(type);
        return registeredType is null ? null : accountNode.Deserialize(registeredType);
    }

    public void SetConfig(string accountId, string type, object config)
    {
        var registeredType = GetRegisteredType(type) ?? throw new ArgumentException($"Type '{type}' is not registered.", nameof(type));

        var accountsNode = _configManager.GetNode(AccountConfig.SavedAccountsConfigKey) ?? new JsonObject();
        if (accountsNode[type] is null)
        {
            accountsNode[type] = new JsonObject();
        }
        accountsNode[type]![accountId] = JsonSerializer.SerializeToNode(config, registeredType);
        _configManager.SetNode(AccountConfig.SavedAccountsConfigKey, accountsNode);

        if (accountId == _accountConfig?.AccountId && type == _accountConfig?.AccountType)
        {
            _currentAccount = config;
            var currentConfig = new AccountConfig { AccountId = accountId, AccountType = type };
            CurrentAccountChanged?.Invoke(currentConfig, config);
        }
    }

    public string CreateAccountId(string type)
    {
        var accountsNode = _configManager.GetNode(AccountConfig.SavedAccountsConfigKey);
        if (accountsNode is null)
        {
            return "1";
        }

        var typeAccounts = accountsNode[type];
        if (typeAccounts is null)
        {
            return "1";
        }

        UInt128 maxId = 0;

        foreach (var accountNode in typeAccounts.AsObject())
        {
            var accountId = accountNode.Key;
            if (UInt128.TryParse(accountId, out var id))
            {
                if (id > maxId)
                {
                    maxId = id;
                }
            }
        }

        return (maxId + 1).ToString();
    }

    public void SelectAccount(AccountConfig accountConfig, AccountSelectionOrigin origin)
    {
        if (accountConfig.Equals(_accountConfig))
        {
            return;
        }

        var account = GetConfig(accountConfig.AccountType, accountConfig.AccountId);
        _accountConfig = accountConfig;
        _currentAccount = account;
        _configManager.SetConfig(accountConfig);

        if (!accountConfig.Equals(_configManager.GetConfig<AccountConfig>()))
        {
            return;
        }

        NotifyCurrentAccountChanged(accountConfig, account);
        AccountSelectionChanged?.Invoke(this, new(accountConfig, origin));
    }

    private void OnAccountConfigChanged()
    {
        var accountConfig = _configManager.GetConfig<AccountConfig>();
        var account = GetConfig(accountConfig.AccountType, accountConfig.AccountId);

        if (!accountConfig.Equals(_accountConfig))
        {
            _accountConfig = accountConfig;
            _currentAccount = account;
            NotifyCurrentAccountChanged(accountConfig, account);
            AccountSelectionChanged?.Invoke(this, new(accountConfig, AccountSelectionOrigin.External));
        }
        else if (!Equals(account, _currentAccount))
        {
            _currentAccount = account;
            NotifyCurrentAccountChanged(accountConfig, account);
        }

        var currentAccounts = GetSavedAccounts().ToList();
        bool changed = _lastSavedAccounts.Count != currentAccounts.Count || !_lastSavedAccounts.SequenceEqual(currentAccounts);
        if (changed)
        {
            _lastSavedAccounts = currentAccounts;
            SavedAccountsChanged?.Invoke(currentAccounts);
        }
    }

    private void NotifyCurrentAccountChanged(AccountConfig accountConfig, object? account)
    {
        if (account is null)
        {
            CurrentAccountChanged?.Invoke(accountConfig, null);
        }
        else
        {
            CurrentAccountChanged?.Invoke(accountConfig, account);
        }
    }

    public IEnumerable<DisplayedAccountConfig> GetSavedAccounts()
    {
        var accountsNode = _configManager.GetNode(AccountConfig.SavedAccountsConfigKey);
        if (accountsNode is null)
            yield break;

        foreach (var typeKvp in accountsNode.AsObject())
        {
            var accountType = typeKvp.Key;
            var typeAccounts = typeKvp.Value;

            if (typeAccounts is null)
                continue;

            foreach (var accountKvp in typeAccounts.AsObject())
            {
                var accountId = accountKvp.Key;
                var displayName = GetAccountDisplayName(accountType, accountId, accountKvp.Value);
                yield return new DisplayedAccountConfig
                {
                    AccountId = accountId,
                    AccountType = accountType,
                    DisplayName = displayName
                };
            }
        }
    }

    private string GetAccountDisplayName(string accountType, string accountId, JsonNode? configNode)
    {
        try
        {
            var configType = GetRegisteredType(accountType);
            if (configType is null || configNode is null)
            {
                return $"{accountId} - {accountType}";
            }

            var config = configNode.Deserialize(configType);
            if (config is null)
            {
                return $"{accountId} - {accountType}";
            }

            if (config is IAdapterConfig adapterConfig)
            {
                if (!string.IsNullOrWhiteSpace(adapterConfig.CustomName))
                    return adapterConfig.CustomName;

                var identify = adapterConfig.NameSuggestion;
                if (!string.IsNullOrWhiteSpace(identify))
                    return identify;
            }

            return $"{accountId} - {accountType}";
        }
        catch (Exception ex)
        {
            _logger.Write($"Error getting account display name: {ex.Message}");
            return $"{accountId} - {accountType}";
        }
    }

    /// <summary>
    /// 删除账户配置
    /// </summary>
    /// <param name="accountType">账户类型</param>
    /// <param name="accountId">账户ID</param>
    /// <returns>是否成功删除</returns>
    public bool RemoveConfig(string accountType, string accountId)
    {
        try
        {
            var accountsNode = _configManager.GetNode(AccountConfig.SavedAccountsConfigKey);
            if (accountsNode is null)
                return false;

            var typeAccounts = accountsNode[accountType];
            if (typeAccounts is null)
                return false;

            var typeAccountsObj = typeAccounts.AsObject();
            if (!typeAccountsObj.ContainsKey(accountId))
                return false;

            typeAccountsObj.Remove(accountId);

            if (typeAccountsObj.Count == 0)
            {
                accountsNode.AsObject().Remove(accountType);
            }

            _configManager.SetNode(AccountConfig.SavedAccountsConfigKey, accountsNode);

            if (_accountConfig?.AccountType == accountType && _accountConfig?.AccountId == accountId)
            {
                var emptyConfig = new AccountConfig { AccountId = string.Empty, AccountType = string.Empty };
                SelectAccount(emptyConfig, AccountSelectionOrigin.Deleted);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Write($"Error removing account config: {ex.Message}");
            return false;
        }
    }
}
