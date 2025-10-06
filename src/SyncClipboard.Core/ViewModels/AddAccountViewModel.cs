using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.RemoteServer.LogInHelper;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public record NavigationInfo(string PageName, string Parameter);

public partial class AddAccountViewModel : ObservableObject
{
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

    public ObservableCollection<string> LoginTypes { get; } = new();

    [ObservableProperty]
    private string selectedType = "";

    [ObservableProperty]
    private string configurationPageName = "";

    [ObservableProperty]
    private NavigationInfo navigationInfo = new("", "");

    partial void OnSelectedTypeChanged(string value)
    {
        LoadConfigurationPageName(value);
    }

    private void LoadConfigurationPageName(string selectedType)
    {
        if (string.IsNullOrEmpty(selectedType))
        {
            ConfigurationPageName = "";
            NavigationInfo = new("", "");
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
        NavigationInfo = new(pageName, selectedType);
    }

    private void LoadAvailableTypes()
    {
        LoginTypes.Clear();
        
        // 从 AccountManager 获取已注册的适配器类型
        foreach (var typeName in _accountManager.GetRegisteredTypeNames())
        {
            if (!LoginTypes.Contains(typeName))
            {
                LoginTypes.Add(typeName);
            }
        }

        // 从 ILogInHelper 获取登录助手类型
        foreach (var helper in _logInHelpers)
        {
            var typeName = helper.TypeName;
            if (!string.IsNullOrEmpty(typeName) && !LoginTypes.Contains(typeName))
            {
                LoginTypes.Add(typeName);
            }
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