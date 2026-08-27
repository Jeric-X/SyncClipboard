using NativeNotification.Interface;
using SyncClipboard.Core.Commons.ConfigMigration;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Shared.Attributes;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    public bool RestoreCurrentConfig() => Save();

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
                exception);
        }

        foreach (var accountTypeNode in savedAccounts)
        {
            var adapterConfig = AccountConfigRegistry.GetRegistration(accountTypeNode.Key)
                ?? throw new SyncClipboardConfigUpgradeException(
                    $"Configuration section '{AccountConfig.SavedAccountsConfigKey}' contains unsupported account type '{accountTypeNode.Key}'.");

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
                    exception);
            }

            foreach (var account in accounts)
            {
                if (account.Value is null)
                {
                    throw new SyncClipboardConfigUpgradeException(
                        $"Account '{accountTypeNode.Key}/{account.Key}' cannot be null.");
                }

                ValidateSection(
                    $"{AccountConfig.SavedAccountsConfigKey}.{accountTypeNode.Key}.{account.Key}",
                    account.Value,
                    adapterConfig.ConfigType);
            }
        }
    }

    private static void ValidateSection(string key, JsonNode node, Type configType)
    {
        try
        {
            var config = node.Deserialize(configType)
                ?? throw new JsonException("The configuration section cannot be null.");

            if (config is IConfigValidator validator)
            {
                validator.Validate();
            }
        }
        catch (Exception exception)
        {
            throw new SyncClipboardConfigUpgradeException(
                $"Configuration section '{key}' is invalid.",
                exception);
        }
    }
}
