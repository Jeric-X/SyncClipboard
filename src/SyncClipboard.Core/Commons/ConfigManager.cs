using System.Collections;
using System.Reflection;
using NativeNotification.Interface;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Shared.Attributes;
using SyncClipboard.Shared.Interfaces;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SyncClipboard.Core.Commons;

public class ConfigManager : ConfigBase
{
    private readonly SyncClipboardConfigUpgrader _configUpgrader;

    private ConfigManager(
        INotificationManager? notification,
        SyncClipboardConfigUpgrader configUpgrader) : base(notification)
    {
        _configUpgrader = configUpgrader;
    }

    public ConfigManager(
        StaticConfig staticConfig,
        INotificationManager notification,
        SyncClipboardConfigUpgrader configUpgrader) : this(notification, configUpgrader)
    {
        bool portableUserConfig = staticConfig.GetConfig<EnvConfig>().PortableUserConfig;
        Path = GetConfigPath(portableUserConfig);
        Reload();
        staticConfig.ListenConfig<EnvConfig>(EnvConfigChanged);
    }

    internal ConfigManager(
        string path,
        SyncClipboardConfigUpgrader configUpgrader) : this((INotificationManager?)null, configUpgrader)
    {
        Path = path;
        Reload();
    }

    public void Reload()
    {
        _configUpgrader.Upgrade(Path);
        Load();
    }

    public static void ValidateConfig(JsonObject root)
    {
        foreach (var config in SyncClipboardConfigRegistry.Configurations
                     .Where(config => config.Storage == ConfigStorage.SyncClipboard))
        {
            if (root[config.Key] is { } node)
            {
                ValidateSection(config.Key, node, config.ConfigType);
            }
        }

        ValidateSavedAccounts(root);
    }

    public bool RestoreCurrentConfig(string? sectionKey)
    {
        if (sectionKey is null)
        {
            return Save();
        }

        try
        {
            SyncClipboardConfigUpgrader.RestoreSection(Path, sectionKey, GetNode(sectionKey));
            return true;
        }
        catch (Exception exception)
        {
            NotificationManager?.ShowText("Failed to restore config section", exception.Message);
            AppCore.TryGetCurrent()?.Logger.Write(
                $"Failed to restore config section '{sectionKey}' to {Path}: {exception}");
            return false;
        }
    }

    private void EnvConfigChanged(EnvConfig envConfig)
    {
        Path = GetConfigPath(envConfig.PortableUserConfig);
        Save();
    }

    public static string GetConfigPath(bool portableUserConfig) =>
        portableUserConfig ? Env.PortableUserConfigFile : Env.UserConfigFile;

    private static void ValidateSavedAccounts(JsonObject root)
    {
        if (root[AccountConfig.SavedAccountsConfigKey] is not { } savedAccountsNode)
        {
            return;
        }

        JsonObject savedAccounts;
        try
        {
            savedAccounts = savedAccountsNode.AsObject();
        }
        catch (Exception exception)
        {
            throw new SyncClipboardConfigUpgradeException(
                $"Configuration section '{AccountConfig.SavedAccountsConfigKey}' must be a JSON object.",
                AccountConfig.SavedAccountsConfigKey,
                exception);
        }

        foreach (var accountTypeNode in savedAccounts)
        {
            var adapterConfig = AccountConfigRegistry.GetRegistration(accountTypeNode.Key)
                ?? throw new SyncClipboardConfigUpgradeException(
                    $"Configuration section '{AccountConfig.SavedAccountsConfigKey}' contains unsupported account type '{accountTypeNode.Key}'.",
                    AccountConfig.SavedAccountsConfigKey);

            JsonObject accounts;
            try
            {
                accounts = accountTypeNode.Value?.AsObject()
                    ?? throw new JsonException("The account collection cannot be null.");
            }
            catch (Exception exception)
            {
                throw new SyncClipboardConfigUpgradeException(
                    $"Account collection '{accountTypeNode.Key}' is invalid.",
                    AccountConfig.SavedAccountsConfigKey,
                    exception);
            }

            foreach (var account in accounts)
            {
                if (account.Value is null)
                {
                    throw new SyncClipboardConfigUpgradeException(
                        $"Account '{accountTypeNode.Key}/{account.Key}' cannot be null.",
                        AccountConfig.SavedAccountsConfigKey);
                }

                ValidateSection(
                    $"{AccountConfig.SavedAccountsConfigKey}.{accountTypeNode.Key}.{account.Key}",
                    account.Value,
                    adapterConfig.ConfigType,
                    AccountConfig.SavedAccountsConfigKey);
            }
        }
    }

    private static void ValidateSection(
        string key,
        JsonNode node,
        Type configType,
        string? recoverableSectionKey = null)
    {
        try
        {
            var config = node.Deserialize(configType)
                ?? throw new JsonException("The configuration section cannot be null.");

            ValidateNonNullableMembers(config, key);

            if (config is IConfigValidator validator)
            {
                validator.Validate();
            }
        }
        catch (Exception exception)
        {
            throw new SyncClipboardConfigUpgradeException(
                $"Configuration section '{key}' is invalid.",
                recoverableSectionKey ?? key,
                exception);
        }
    }

    private static void ValidateNonNullableMembers(object config, string path)
    {
        var nullabilityContext = new NullabilityInfoContext();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        ValidateObject(config, path, nullabilityContext, visited);
    }

    private static void ValidateObject(
        object value,
        string path,
        NullabilityInfoContext nullabilityContext,
        HashSet<object> visited)
    {
        var valueType = value.GetType();
        if (IsTerminalType(valueType)
            || (!valueType.IsValueType && !visited.Add(value)))
        {
            return;
        }

        foreach (var property in valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetMethod is null
                || property.GetMethod.IsStatic
                || property.GetIndexParameters().Length != 0
                || property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition == JsonIgnoreCondition.Always)
            {
                continue;
            }

            var nullability = nullabilityContext.Create(property);
            ValidateValue(
                property.GetValue(value),
                nullability,
                $"{path}.{property.Name}",
                nullabilityContext,
                visited);
        }
    }

    private static void ValidateValue(
        object? value,
        NullabilityInfo? nullability,
        string path,
        NullabilityInfoContext nullabilityContext,
        HashSet<object> visited)
    {
        if (value is null)
        {
            ValidateNullValue(nullability, path);
            return;
        }

        var valueType = value.GetType();
        ValidateEnumValue(value, valueType, path);
        if (IsTerminalType(valueType))
        {
            return;
        }

        if (value is IDictionary dictionary)
        {
            ValidateDictionary(dictionary, nullability, path, nullabilityContext, visited);
            return;
        }

        if (value is IEnumerable enumerable)
        {
            ValidateEnumerable(enumerable, nullability, path, nullabilityContext, visited);
            return;
        }

        ValidateObject(value, path, nullabilityContext, visited);
    }

    private static void ValidateNullValue(NullabilityInfo? nullability, string path)
    {
        if (nullability?.ReadState == NullabilityState.NotNull)
        {
            throw new JsonException($"Configuration value '{path}' cannot be null.");
        }
    }

    private static void ValidateEnumValue(object value, Type valueType, string path)
    {
        if (valueType.IsEnum && !Enum.IsDefined(valueType, value))
        {
            throw new JsonException(
                $"Configuration value '{path}' contains undefined {valueType.Name} value '{value}'.");
        }
    }

    private static void ValidateDictionary(
        IDictionary dictionary,
        NullabilityInfo? nullability,
        string path,
        NullabilityInfoContext nullabilityContext,
        HashSet<object> visited)
    {
        var genericArguments = nullability?.GenericTypeArguments;
        var keyNullability = genericArguments is { Length: 2 } ? genericArguments[0] : null;
        var valueNullability = genericArguments is { Length: 2 } ? genericArguments[1] : null;

        foreach (DictionaryEntry entry in dictionary)
        {
            var itemPath = $"{path}[{entry.Key}]";
            ValidateValue(entry.Key, keyNullability, $"{itemPath}.Key", nullabilityContext, visited);
            ValidateValue(entry.Value, valueNullability, itemPath, nullabilityContext, visited);
        }
    }

    private static void ValidateEnumerable(
        IEnumerable enumerable,
        NullabilityInfo? nullability,
        string path,
        NullabilityInfoContext nullabilityContext,
        HashSet<object> visited)
    {
        var elementNullability = nullability?.ElementType
            ?? (nullability?.GenericTypeArguments is { Length: 1 } genericArguments
                ? genericArguments[0]
                : null);
        var index = 0;
        foreach (var item in enumerable)
        {
            ValidateValue(item, elementNullability, $"{path}[{index}]", nullabilityContext, visited);
            index++;
        }
    }

    private static bool IsTerminalType(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || type == typeof(DateTime)
        || type == typeof(DateTimeOffset)
        || type == typeof(TimeSpan)
        || type == typeof(Guid)
        || type == typeof(Uri);
}
