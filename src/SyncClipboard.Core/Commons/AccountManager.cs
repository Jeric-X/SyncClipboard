using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Core.Attributes;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Commons;

public class AccountManager
{
    public delegate void AccountChangedHandler(AccountConfig accountConfig, object? config);
    public event AccountChangedHandler? CurrentAccountChanged;

    public delegate void SavedAccountsChangedHandler(IEnumerable<DisplayedAccountConfig> newAccounts);
    public event SavedAccountsChangedHandler? SavedAccountsChanged;

    private const string Accounts = "SavedAccounts";
    private AccountConfig? _accountConfig;
    private object? _currentAccount;
    private List<DisplayedAccountConfig> _lastSavedAccounts = [];

    private readonly ConfigManager _configManager;
    private readonly Dictionary<string, Type> _registedTypeList = [];
    private readonly ILogger _logger;

    public AccountManager(ConfigManager configManager, ILogger logger)
    {
        _configManager = configManager;
        _logger = logger;
        ScanAdapterConfigTypes();

        OnAccountConfigChanged();
        _configManager.ConfigChanged += OnAccountConfigChanged;
    }

    /// <summary>
    /// 扫描程序集中所有继承自IAdapterConfig<T>的类并注册
    /// </summary>
    private void ScanAdapterConfigTypes()
    {
        try
        {
            var currentAssembly = Assembly.GetExecutingAssembly();
            var types = currentAssembly.GetTypes();
            var adapterConfigInterface = typeof(IAdapterConfig<>);

            foreach (var type in types)
            {
                // 检查类型是否实现了IAdapterConfig<T>接口
                var interfaceType = type.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == adapterConfigInterface);

                if (interfaceType != null)
                {
                    // 通过反射调用静态属性TypeName
                    var typeNameProperty = interfaceType.GetProperty("TypeName", BindingFlags.Static | BindingFlags.Public);
                    if (typeNameProperty != null)
                    {
                        var typeName = (string?)typeNameProperty.GetValue(null);
                        if (!string.IsNullOrEmpty(typeName))
                        {
                            RegistConfigType(typeName, type);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Write("AccountManager", $"Error scanning IAdapterConfig types: {ex.Message}");
        }
    }

    public Type? GetRegisteredType(string typeName)
    {
        return _registedTypeList.GetValueOrDefault(typeName);
    }

    public IEnumerable<string> GetRegisteredTypeNames()
    {
        return _registedTypeList.Keys;
    }

    public object? GetConfig(string type, string accountId)
    {
        var accountsNode = _configManager.GetNode(Accounts);
        if (accountsNode is null)
            return null;

        var typeAccounts = accountsNode[type];
        if (typeAccounts is null)
            return null;

        var accountNode = typeAccounts[accountId];
        if (accountNode is null)
            return null;

        return accountNode.Deserialize(_registedTypeList[type]);
    }

    public void SetConfig(string accountId, string type, object config)
    {
        var registeredType = GetRegisteredType(type) ?? throw new ArgumentException($"Type '{type}' is not registered.", nameof(type));

        var accountsNode = _configManager.GetNode(Accounts) ?? new JsonObject();
        if (accountsNode[type] is null)
        {
            accountsNode[type] = new JsonObject();
        }
        accountsNode[type]![accountId] = JsonSerializer.SerializeToNode(config, registeredType);
        _configManager.SetNode(Accounts, accountsNode);

        if (accountId == _accountConfig?.AccountId && type == _accountConfig?.AccountType)
        {
            _currentAccount = config;
            var currentConfig = new AccountConfig { AccountId = accountId, AccountType = type };
            CurrentAccountChanged?.Invoke(currentConfig, config);
        }
    }

    public void RegistConfigType(string key, Type type)
    {
        if (_registedTypeList.Contains(new KeyValuePair<string, Type>(key, type)))
        {
            return;
        }
        else if (!_registedTypeList.TryAdd(key, type))
        {
            _registedTypeList[key] = type;
        }
    }

    public string CreateAccountId(string type)
    {
        var accountsNode = _configManager.GetNode(Accounts);
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

    private void OnAccountConfigChanged()
    {
        var accountConfig = _configManager.GetConfig<AccountConfig>();
        var account = GetConfig(accountConfig.AccountType, accountConfig.AccountId);

        if (!accountConfig.Equals(_accountConfig))
        {
            _accountConfig = accountConfig;
            _currentAccount = account;
            NotifyCurrentAccountChanged(accountConfig, account);
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
        var accountsNode = _configManager.GetNode(Accounts);
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
            if (!_registedTypeList.TryGetValue(accountType, out var configType) || configNode is null)
            {
                return $"{accountId} - {accountType}";
            }

            var config = configNode.Deserialize(configType);
            if (config is null)
            {
                return $"{accountId} - {accountType}";
            }

            var userNameProperty = configType.GetProperties()
                .FirstOrDefault(p => p.GetCustomAttribute<UserNameAttribute>() != null);

            if (userNameProperty != null)
            {
                var userName = userNameProperty.GetValue(config)?.ToString();
                if (!string.IsNullOrEmpty(userName))
                {
                    return $"{userName} - {accountType}";
                }
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
            var accountsNode = _configManager.GetNode(Accounts);
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

            _configManager.SetNode(Accounts, accountsNode);

            if (_accountConfig?.AccountType == accountType && _accountConfig?.AccountId == accountId)
            {
                var emptyConfig = new AccountConfig { AccountId = string.Empty, AccountType = string.Empty };
                _configManager.SetConfig(emptyConfig);
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