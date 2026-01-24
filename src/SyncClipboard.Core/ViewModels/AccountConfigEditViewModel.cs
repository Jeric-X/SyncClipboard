using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Attributes;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using System.Collections.ObjectModel;
using System.Reflection;

namespace SyncClipboard.Core.ViewModels;

public partial class AccountConfigEditViewModel(
    AccountManager accountManager,
    RemoteClipboardServerFactory serverFactory,
    ConfigManager configManager,
    MainViewModel mainViewModel) : ObservableObject
{
    private readonly AccountManager _accountManager = accountManager;
    private readonly RemoteClipboardServerFactory _serverFactory = serverFactory;
    private readonly ConfigManager _configManager = configManager;
    private readonly MainViewModel _mainViewModel = mainViewModel;
    private CancellationTokenSource? _testCancellationTokenSource;

    [ObservableProperty]
    private string accountType = "";

    [ObservableProperty]
    private string accountId = "";

    [ObservableProperty]
    private bool isLoading = false;

    [ObservableProperty]
    private bool? testResult = null; // null=未测试, true=成功, false=失败

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool hasError = false;

    public bool ShowTestResult => TestResult.HasValue;
    public bool IsTestSuccess => TestResult == true;
    public bool IsTestFailure => TestResult == false;
    public bool IsTestPending => TestResult == null;
    public bool CanCancelTest => IsLoading && _testCancellationTokenSource != null;

    public ObservableCollection<PropertyInputViewModel> Properties { get; } = [];

    public void LoadProperties(AccountConfig accountConfig)
    {
        TestResult = null;
        ErrorMessage = null;
        HasError = false;
        NotifyTestResultPropertiesChanged();

        AccountType = accountConfig.AccountType;
        AccountId = accountConfig.AccountId;
        Properties.Clear();

        if (string.IsNullOrEmpty(accountConfig.AccountType))
            return;

        var registeredType = _accountManager.GetRegisteredType(accountConfig.AccountType);
        if (registeredType == null)
        {
            return;
        }

        var supportedTypes = new[] {
            typeof(string), typeof(int), typeof(uint), typeof(double), typeof(decimal), typeof(float), typeof(long), typeof(ulong), typeof(short), typeof(ushort), typeof(bool),
            typeof(int?), typeof(uint?), typeof(double?), typeof(decimal?), typeof(float?), typeof(long?), typeof(ulong?), typeof(short?), typeof(ushort?), typeof(bool?)
        };
        var properties = registeredType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && supportedTypes.Contains(p.PropertyType))
            .ToList();

        object? sourceConfig = null;
        if (!string.IsNullOrEmpty(accountConfig.AccountId))
        {
            sourceConfig = _accountManager.GetConfig(accountConfig.AccountType, accountConfig.AccountId);
        }

        if (sourceConfig == null)
        {
            try
            {
                sourceConfig = Activator.CreateInstance(registeredType);
            }
            catch
            {
            }
        }

        foreach (var property in properties)
        {
            var displayAttribute = property.GetCustomAttribute<PropertyDisplayAttribute>();

            var propertyInput = new PropertyInputViewModel
            {
                PropertyName = property.Name,
                DisplayName = GetDisplayName(property, displayAttribute),
                PropertyType = property.PropertyType,
                InputType = GetPropertyInputType(property, displayAttribute),
                Description = GetDescriptionText(displayAttribute)
            };

            if (sourceConfig != null)
            {
                try
                {
                    var value = property.GetValue(sourceConfig);
                    if (value != null)
                    {
                        propertyInput.SetTypedValue(value);
                    }
                }
                catch
                {
                }
            }

            Properties.Add(propertyInput);
        }
    }

    private static string GetDisplayName(PropertyInfo property, PropertyDisplayAttribute? displayAttribute)
    {
        if (displayAttribute != null && !string.IsNullOrEmpty(displayAttribute.DisplayName))
        {
            var localizedName = TryGetLocalizedString(displayAttribute.DisplayName);
            return !string.IsNullOrEmpty(localizedName) ? localizedName : displayAttribute.DisplayName;
        }

        return property.Name;
    }

    private static string? GetDescriptionText(PropertyDisplayAttribute? displayAttribute)
    {
        if (displayAttribute?.Description != null)
        {
            var localizedDescription = TryGetLocalizedString(displayAttribute.Description);
            return !string.IsNullOrEmpty(localizedDescription) ? localizedDescription : displayAttribute.Description;
        }
        return null;
    }

    private static string? TryGetLocalizedString(string key)
    {
        try
        {
            var result = Strings.ResourceManager.GetString(key);
            if (!string.IsNullOrEmpty(result))
            {
                return result;
            }
        }
        catch { }
        return null;
    }

    private static PropertyInputType GetPropertyInputType(PropertyInfo property, PropertyDisplayAttribute? displayAttribute)
    {
        var baseType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (baseType == typeof(bool))
            return PropertyInputType.Boolean;

        if (baseType == typeof(int) || baseType == typeof(uint) || baseType == typeof(long) || baseType == typeof(ulong) || baseType == typeof(short) || baseType == typeof(ushort))
            return PropertyInputType.Integer;

        if (baseType == typeof(double) || baseType == typeof(decimal) || baseType == typeof(float))
            return PropertyInputType.Decimal;

        if (displayAttribute?.IsPassword == true)
            return PropertyInputType.Password;

        return PropertyInputType.Text;
    }

    private void NotifyTestResultPropertiesChanged()
    {
        OnPropertyChanged(nameof(ShowTestResult));
        OnPropertyChanged(nameof(IsTestSuccess));
        OnPropertyChanged(nameof(IsTestFailure));
        OnPropertyChanged(nameof(IsTestPending));
        OnPropertyChanged(nameof(CanCancelTest));
    }

    private object? CreateConfigInstance()
    {
        var configType = _accountManager.GetRegisteredType(AccountType);
        if (configType == null)
        {
            return null;
        }

        var configInstance = Activator.CreateInstance(configType);
        if (configInstance == null)
        {
            return null;
        }

        foreach (var property in Properties)
        {
            try
            {
                var propertyInfo = configType.GetProperty(property.PropertyName);
                if (propertyInfo != null && propertyInfo.CanWrite)
                {
                    var value = property.GetTypedValue();
                    propertyInfo.SetValue(configInstance, value);
                }
            }
            catch
            {
                return null;
            }
        }

        return configInstance;
    }

    private bool ValidateProperties()
    {
        bool isValid = true;
        foreach (var property in Properties)
        {
            if (!property.IsValid)
            {
                property.ErrorMessage = Strings.PleaseEnterValidValue;
                isValid = false;
            }
            else
            {
                property.ErrorMessage = null;
            }
        }

        if (!isValid)
        {
            ErrorMessage = Strings.CheckAndCorrectErrors;
            HasError = true;
        }

        return isValid;
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        // 如果已经在测试中，先取消之前的测试
        _testCancellationTokenSource?.Cancel();
        _testCancellationTokenSource?.Dispose();

        _testCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _testCancellationTokenSource.Token;

        IsLoading = true;
        TestResult = null;
        ErrorMessage = null;
        HasError = false;

        NotifyTestResultPropertiesChanged();

        // 验证属性有效性
        if (!ValidateProperties())
        {
            IsLoading = false;
            TestResult = false;
            NotifyTestResultPropertiesChanged();
            return;
        }

        try
        {
            var adapter = _serverFactory.GetAdapter(AccountType) ?? throw new InvalidOperationException(Strings.NoAdapterFound);
            var configInstance = CreateConfigInstance() ?? throw new InvalidOperationException(Strings.CannotCreateConfigInstance);
            var syncConfig = _configManager.GetConfig<SyncConfig>();
            adapter.SetConfig(configInstance, syncConfig);
            adapter.ApplyConfig();

            await adapter.TestConnectionAsync(cancellationToken);
            TestResult = true;
        }
        catch (OperationCanceledException)
        {
            TestResult = false;
            ErrorMessage = Strings.TestCancelled;
            HasError = true;
        }
        catch (Exception ex)
        {
            TestResult = false;
            ErrorMessage = ex is InvalidOperationException
                ? ex.Message
                : string.Format(Strings.TestConnectionError, ex.Message);
            HasError = true;
        }
        finally
        {
            _testCancellationTokenSource?.Dispose();
            _testCancellationTokenSource = null;
            IsLoading = false;
            NotifyTestResultPropertiesChanged();
        }
    }

    [RelayCommand]
    private void CancelTest()
    {
        _testCancellationTokenSource?.Cancel();
    }

    [RelayCommand]
    private void Confirm()
    {
        // 清除之前的错误信息
        ErrorMessage = null;
        HasError = false;

        // 验证所有属性的格式正确性
        if (!ValidateProperties())
        {
            return; // 验证失败，不继续执行
        }

        try
        {
            var configInstance = CreateConfigInstance() ?? throw new InvalidOperationException(Strings.CannotCreateConfigInstance);
            _accountManager.SetConfig(AccountId, AccountType, configInstance);
            _mainViewModel.NavigateToLastLevel();
        }
        catch (Exception ex)
        {
            // 显示错误信息
            ErrorMessage = string.Format(Strings.SaveConfigFailed, ex.Message);
            HasError = true;
        }
    }

    public Dictionary<string, object?> GetPropertyValues()
    {
        var values = new Dictionary<string, object?>();
        foreach (var property in Properties)
        {
            values[property.PropertyName] = property.GetTypedValue();
        }
        return values;
    }
}