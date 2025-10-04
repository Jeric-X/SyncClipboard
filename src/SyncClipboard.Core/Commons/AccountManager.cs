using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using SyncClipboard.Core.RemoteServer.Adapter;

namespace SyncClipboard.Core.Commons;

public class AccountManager
{
    public delegate void AccountChangedHandler(string type, string? accountId, object? config);
    public event AccountChangedHandler? CurrentAccountChanged;

    private const string Accounts = "SavedAccounts";
    private AccountConfig? _accountConfig;
    private object? _currentAccount;

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
        var accountsNode = _configManager.GetNode(Accounts) ?? new JsonObject();
        var typeAccounts = accountsNode[type] ?? new JsonObject();
        typeAccounts[accountId] = JsonSerializer.SerializeToNode(config);
        _configManager.SetNode(Accounts, accountsNode);

        if (accountId == _accountConfig?.AccountId && type == _accountConfig?.AccountType)
        {
            _currentAccount = config;
            CurrentAccountChanged?.Invoke(type, accountId, config);
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

    private void OnAccountConfigChanged()
    {
        var accountConfig = _configManager.GetConfig<AccountConfig>();
        var account = GetConfig(accountConfig.AccountType, accountConfig.AccountId);

        if (!accountConfig.Equals(_accountConfig))
        {
            _accountConfig = accountConfig;
            _currentAccount = account;
            NotifyCurrentAccountChanged(accountConfig.AccountType, accountConfig.AccountId, account);
        }
        else if (!Equals(account, _currentAccount))
        {
            _currentAccount = account;
            NotifyCurrentAccountChanged(accountConfig.AccountType, accountConfig.AccountId, account);
        }
    }

    private void NotifyCurrentAccountChanged(string accountType, string? accountId, object? account)
    {
        if (account is null)
        {
            CurrentAccountChanged?.Invoke(string.Empty, null, null);
        }
        else
        {
            CurrentAccountChanged?.Invoke(accountType, accountId, account);
        }
    }

    // /// <summary>
    // /// 获取所有账户名称
    // /// </summary>
    // /// <returns>账户名称列表</returns>
    // public IEnumerable<string> GetaccountIds()
    // {
    //     var accountConfig = _configManager.GetConfig<AccountsConfig>();
    //     return accountConfig.Accounts.Keys;
    // }

    // /// <summary>
    // /// 删除账户配置
    // /// </summary>
    // /// <param name="accountId">账户名称</param>
    // /// <returns>是否成功删除</returns>
    // public bool RemoveConfig(string accountId)
    // {
    //     ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

    //     var accountConfig = _configManager.GetConfig<AccountsConfig>();

    //     if (!accountConfig.Accounts.ContainsKey(accountId))
    //     {
    //         return false;
    //     }

    //     accountConfig.Accounts.Remove(accountId);
    //     _configManager.SetConfig(accountConfig);

    //     // 清理监听器
    //     _listeners.Remove(accountId);

    //     return true;
    // }
}