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

                if (IsConfigurationError(exception))
                {
                    if (!await TryRecoverAsync(configPathProvider(), exception))
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

    public async Task<bool> TryRestoreCurrentConfigAsync(
        string configPath,
        Func<bool> restoreCurrentConfig,
        Exception reloadException)
    {
        logger.Write(LogTag, $"Configuration reload failed: {reloadException}");
        logger.Flush();

        var restore = await globalDialog.ShowConfirmationAsync(
            Strings.ReloadConfigFailed,
            string.Format(Strings.ReloadConfigRecoveryMessage, configPath, reloadException.Message),
            Strings.RestoreCurrentConfig,
            Strings.Exit);
        if (!restore)
        {
            logger.Write(LogTag, "Configuration restore declined.");
            logger.Flush();
            return false;
        }

        var configBackedUp = false;
        while (true)
        {
            if (!configBackedUp)
            {
                try
                {
                    var backupPath = SyncClipboardConfigUpgrader.BackupConfig(configPath);
                    configBackedUp = true;
                    if (backupPath is not null)
                    {
                        logger.Write(LogTag, $"Backed up invalid configuration '{configPath}' to '{backupPath}'.");
                        logger.Flush();
                    }
                }
                catch (Exception backupException)
                {
                    logger.Write(LogTag, $"Failed to back up invalid configuration '{configPath}': {backupException}");
                    logger.Flush();
                    var retryBackup = await globalDialog.ShowConfirmationAsync(
                        Strings.ReloadConfigFailed,
                        string.Format(Strings.OperationFailedRetryMessage, backupException.Message),
                        Strings.Retry,
                        Strings.Exit);
                    if (!retryBackup)
                    {
                        return false;
                    }

                    continue;
                }
            }

            if (restoreCurrentConfig())
            {
                logger.Write(LogTag, $"Restored the active configuration to '{configPath}'.");
                logger.Flush();
                return true;
            }

            logger.Write(LogTag, $"Failed to restore the active configuration to '{configPath}'.");
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

    public async Task<bool> TryRecoverAsync(string configPath, Exception exception)
    {
        var configError = GetConfigurationError(exception)
            ?? throw new ArgumentException("The exception is not a configuration upgrade error.", nameof(exception));

        var overwrite = await globalDialog.ShowConfirmationAsync(
            Strings.ConfigurationErrorTitle,
            string.Format(Strings.ConfigurationErrorMessage, configPath, configError.Message),
            Strings.OverwriteConfig,
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
                SyncClipboardConfigUpgrader.ReplaceWithDefault(configPath);
                logger.Write(LogTag, $"Replaced invalid configuration '{configPath}' with defaults after backing it up.");
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
}
