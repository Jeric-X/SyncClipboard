using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Core.RemoteServer.LogInHelper;
using System.Collections.ObjectModel;
using System.Reflection;

namespace SyncClipboard.Core.ViewModels;

public partial class AddAccountViewModel : ObservableObject
{
    public record NavigationInfoType(string PageName, string Parameter);

    private readonly ConfigManager _configManager;
    private readonly AccountManager _accountManager;
    private readonly MainViewModel _mainVM;
    private readonly IEnumerable<ILoginHelper> _logInHelpers;

    public AddAccountViewModel(ConfigManager configManager, AccountManager accountManager, MainViewModel mainViewModel, IEnumerable<ILoginHelper> logInHelpers)
    {
        _configManager = configManager;
        _accountManager = accountManager;
        _mainVM = mainViewModel;
        _logInHelpers = logInHelpers;
        LoadAvailableTypes();
    }

    public ObservableCollection<string> LoginTypes { get; } = [];

    [ObservableProperty]
    private string selectedType = "";

    [ObservableProperty]
    private string configurationPageName = "";

    [ObservableProperty]
    private NavigationInfoType navigationInfo = new("", "");

    partial void OnSelectedTypeChanged(string value)
    {
        LoadConfigurationPageName(value);
    }

    private void LoadConfigurationPageName(string selectedType)
    {
        if (string.IsNullOrEmpty(selectedType))
        {
            ConfigurationPageName = "";
            NavigationInfo = new NavigationInfoType("", "");
            return;
        }

        // 检查选择的类型是否来源于 ILogInHelper
        var loginHelper = _logInHelpers.FirstOrDefault(helper => helper.TypeName == selectedType);

        string pageName;
        if (loginHelper != null)
        {
            // 来源于 ILogInHelper 的类型使用其指定的登录页面
            pageName = loginHelper.LoginPageName;
        }
        else
        {
            // 其他类型使用默认配置页面
            pageName = PageDefinition.DefaultAddAccount.Name + "Page";
        }

        ConfigurationPageName = pageName;
        NavigationInfo = new NavigationInfoType(pageName, selectedType);
    }

    private void LoadAvailableTypes()
    {
        LoginTypes.Clear();

        // 收集所有账号类型及其优先级
        var typeWithPriority = new Dictionary<string, int>();

        // 从 AccountManager 获取已注册的适配器类型
        foreach (var typeName in _accountManager.GetRegisteredTypeNames())
        {
            if (!typeWithPriority.ContainsKey(typeName))
            {
                var registeredType = _accountManager.GetRegisteredType(typeName);
                if (registeredType != null)
                {
                    // 通过反射获取Priority属性
                    var adapterConfigInterface = registeredType.GetInterfaces()
                        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAdapterConfig<>));

                    if (adapterConfigInterface != null)
                    {
                        var priorityProperty = adapterConfigInterface.GetProperty("Priority", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                        var priority = (int?)priorityProperty?.GetValue(null) ?? int.MaxValue;
                        typeWithPriority[typeName] = priority;
                    }
                    else
                    {
                        typeWithPriority[typeName] = int.MaxValue;
                    }
                }
            }
        }

        // 从 ILogInHelper 获取登录助手类型
        foreach (var helper in _logInHelpers)
        {
            var typeName = helper.TypeName;
            if (!string.IsNullOrEmpty(typeName) && !typeWithPriority.ContainsKey(typeName))
            {
                typeWithPriority[typeName] = helper.Priority;
            }
        }

        // 按优先级排序，优先级相同则按名称字母顺序排序
        var sortedTypes = typeWithPriority
            .OrderBy(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key)
            .Select(kvp => kvp.Key);

        // 将排序后的类型添加到LoginTypes
        foreach (var typeName in sortedTypes)
        {
            LoginTypes.Add(typeName);
        }

        // 如果有可用类型，默认选择第一个
        if (LoginTypes.Count > 0)
        {
            SelectedType = LoginTypes[0];
            // 确保在初始化时也触发配置界面的加载逻辑
            LoadConfigurationPageName(SelectedType);
        }
    }
}