using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Commons.ConfigMigration;

public sealed class ConfigRecoveryService(
    IGlobalDialog globalDialog,
    ILogger logger)
{
    private const string LogTag = nameof(ConfigRecoveryService);

    public async Task<T?> ExecuteWithRecoveryAsync<T>(
        Func<T> operation,
        Func<string> configPathProvider,
        string failureTitle,
        string operationName)
    {
        while (true)
        {
            try
            {
                return operation();
            }
            catch (Exception exception)
            {
                logger.Write(LogTag, $"{operationName} failed: {exception}");
                logger.Flush();

                if (IsRegistrationError(exception))
                {
                    logger.Write(
                        LogTag,
                        $"{operationName} cannot continue because configuration registration failed. The application will exit.");
                    logger.Flush();
                    return default;
                }

                if (IsConfigurationError(exception))
                {
                    string configPath;
                    try
                    {
                        configPath = configPathProvider();
                    }
                    catch (Exception pathException)
                    {
                        logger.Write(
                            LogTag,
                            $"Failed to determine the configuration path while handling '{operationName}': {pathException}");
                        logger.Flush();
                        return default;
                    }

                    if (!await TryRecoverAsync(configPath, exception))
                    {
                        return default;
                    }
                    continue;
                }

                var retry = await globalDialog.ShowConfirmationAsync(
                    failureTitle,
                    string.Format(Strings.OperationFailedRetryMessage, exception.Message),
                    Strings.Retry,
                    Strings.Exit);
                if (!retry)
                {
                    logger.Write(LogTag, $"{operationName} retry declined.");
                    logger.Flush();
                    return default;
                }
            }
        }
    }

    public static bool IsConfigurationError(Exception exception) =>
        GetConfigurationError(exception) is not null;

    public static bool IsRegistrationError(Exception exception) =>
        GetRegistrationError(exception) is not null;

    public async Task<bool> TryRestoreCurrentConfigAsync(
        string configPath,
        Func<string?, bool> restoreCurrentConfig,
        Exception reloadException)
    {
        logger.Write(LogTag, $"Configuration reload failed: {reloadException}");
        logger.Flush();

        if (IsRegistrationError(reloadException))
        {
            logger.Write(
                LogTag,
                "Configuration reload cannot continue because configuration registration failed. The application will exit.");
            logger.Flush();
            return false;
        }

        var configError = GetConfigurationError(reloadException);
        var sectionKey = configError?.RecoverableSectionKey;
        if (!await ConfirmRestoreAsync(configPath, sectionKey, reloadException))
        {
            logger.Write(LogTag, "Configuration restore declined.");
            logger.Flush();
            return false;
        }

        if (!await TryBackupInvalidConfigAsync(configPath))
        {
            return false;
        }

        return await TryRestoreConfigAsync(configPath, sectionKey, restoreCurrentConfig);
    }

    private Task<bool> ConfirmRestoreAsync(
        string configPath,
        string? sectionKey,
        Exception reloadException)
    {
        var recoveryMessage = sectionKey is null
            ? string.Format(Strings.ReloadConfigRecoveryMessage, configPath, reloadException.Message)
            : string.Format(
                Strings.ReloadConfigSectionRecoveryMessage,
                configPath,
                sectionKey,
                reloadException.Message);
        var primaryButtonText = sectionKey is null
            ? Strings.RestoreCurrentConfig
            : Strings.RestoreCurrentConfigSection;

        return globalDialog.ShowConfirmationAsync(
            Strings.ReloadConfigFailed,
            recoveryMessage,
            primaryButtonText,
            Strings.Exit);
    }

    private async Task<bool> TryBackupInvalidConfigAsync(string configPath)
    {
        while (true)
        {
            try
            {
                var backupPath = SyncClipboardConfigUpgrader.BackupConfig(configPath);
                if (backupPath is not null)
                {
                    logger.Write(LogTag, $"Backed up invalid configuration '{configPath}' to '{backupPath}'.");
                    logger.Flush();
                }

                return true;
            }
            catch (Exception backupException)
            {
                logger.Write(LogTag, $"Failed to back up invalid configuration '{configPath}': {backupException}");
                logger.Flush();
                var retry = await globalDialog.ShowConfirmationAsync(
                    Strings.ReloadConfigFailed,
                    string.Format(Strings.OperationFailedRetryMessage, backupException.Message),
                    Strings.Retry,
                    Strings.Exit);
                if (!retry)
                {
                    return false;
                }
            }
        }
    }

    private async Task<bool> TryRestoreConfigAsync(
        string configPath,
        string? sectionKey,
        Func<string?, bool> restoreCurrentConfig)
    {
        while (true)
        {
            if (restoreCurrentConfig(sectionKey))
            {
                logger.Write(LogTag, GetRestoreLogMessage(configPath, sectionKey, succeeded: true));
                logger.Flush();
                return true;
            }

            logger.Write(LogTag, GetRestoreLogMessage(configPath, sectionKey, succeeded: false));
            logger.Flush();
            var retry = await globalDialog.ShowConfirmationAsync(
                Strings.ReloadConfigFailed,
                string.Format(
                    Strings.OperationFailedRetryMessage,
                    string.Format(Strings.SaveConfigFailed, configPath)),
                Strings.Retry,
                Strings.Exit);
            if (!retry)
            {
                return false;
            }
        }
    }

    private static string GetRestoreLogMessage(
        string configPath,
        string? sectionKey,
        bool succeeded)
    {
        if (sectionKey is null)
        {
            return succeeded
                ? $"Restored the active configuration to '{configPath}'."
                : $"Failed to restore the active configuration to '{configPath}'.";
        }

        return succeeded
            ? $"Restored active configuration section '{sectionKey}' to '{configPath}'."
            : $"Failed to restore active configuration section '{sectionKey}' to '{configPath}'.";
    }

    public async Task<bool> TryRecoverAsync(string configPath, Exception exception)
    {
        var configError = GetConfigurationError(exception)
            ?? throw new ArgumentException("The exception is not a configuration upgrade error.", nameof(exception));
        var sectionKey = configError.RecoverableSectionKey;
        var recoveryMessage = sectionKey is null
            ? string.Format(Strings.ConfigurationErrorMessage, configPath, configError.Message)
            : string.Format(
                Strings.ConfigurationSectionErrorMessage,
                configPath,
                sectionKey,
                configError.Message);

        var overwrite = await globalDialog.ShowConfirmationAsync(
            Strings.ConfigurationErrorTitle,
            recoveryMessage,
            sectionKey is null ? Strings.OverwriteConfig : Strings.OverwriteConfigSection,
            Strings.Exit);
        if (!overwrite)
        {
            logger.Write(LogTag, $"Configuration recovery declined for '{configPath}'.");
            logger.Flush();
            return false;
        }

        while (true)
        {
            try
            {
                if (sectionKey is null)
                {
                    SyncClipboardConfigUpgrader.ReplaceWithDefault(configPath);
                    logger.Write(
                        LogTag,
                        $"Replaced invalid configuration '{configPath}' with defaults after backing it up.");
                }
                else
                {
                    SyncClipboardConfigUpgrader.ReplaceSectionWithDefault(configPath, sectionKey);
                    logger.Write(
                        LogTag,
                        $"Replaced invalid configuration section '{sectionKey}' in '{configPath}' with defaults after backing it up.");
                }
                logger.Flush();
                return true;
            }
            catch (Exception resetException)
            {
                logger.Write(LogTag, $"Failed to replace invalid configuration '{configPath}': {resetException}");
                logger.Flush();
                var retry = await globalDialog.ShowConfirmationAsync(
                    Strings.ConfigurationErrorTitle,
                    string.Format(Strings.ConfigurationResetFailedMessage, configPath, resetException.Message),
                    Strings.Retry,
                    Strings.Exit);
                if (!retry)
                {
                    return false;
                }
            }
        }
    }

    private static SyncClipboardConfigUpgradeException? GetConfigurationError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SyncClipboardConfigUpgradeException configException)
            {
                return configException;
            }
        }

        return null;
    }

    private static ConfigRegistrationException? GetRegistrationError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is ConfigRegistrationException registrationException)
            {
                return registrationException;
            }
        }

        return null;
    }
}
