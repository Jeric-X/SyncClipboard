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
