using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SyncClipboard.Core.Commons.ConfigMigration;

public sealed class SyncClipboardConfigUpgrader
{
    public const string VersionPropertyName = "ConfigVersion";

    private const int BackupRetentionCount = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly Dictionary<int, ISyncClipboardConfigMigration> _migrations;

    public SyncClipboardConfigUpgrader()
        : this([new SyncClipboardConfigMigrationV0ToV1()])
    {
    }

    public SyncClipboardConfigUpgrader(IEnumerable<ISyncClipboardConfigMigration> migrations)
    {
        var migrationList = migrations.ToArray();
        if (migrationList.Any(migration => migration.ToVersion != migration.FromVersion + 1))
        {
            throw new ArgumentException("Configuration migrations must advance exactly one version.", nameof(migrations));
        }

        try
        {
            _migrations = migrationList.ToDictionary(migration => migration.FromVersion);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("Only one configuration migration can start from each version.", nameof(migrations), exception);
        }
    }

    public void Upgrade(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        var directory = Path.GetDirectoryName(configPath)
            ?? throw new SyncClipboardConfigUpgradeException($"Cannot determine the configuration directory for '{configPath}'.");
        Directory.CreateDirectory(directory);

        using var migrationLock = AcquireMigrationLock(configPath);

        if (!File.Exists(configPath))
        {
            AtomicWrite(configPath, new JsonObject
            {
                [VersionPropertyName] = Env.SyncClipboardConfigVersion,
            });
            return;
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject
                ?? throw new SyncClipboardConfigUpgradeException("The root of SyncClipboard.json must be a JSON object.");
        }
        catch (SyncClipboardConfigUpgradeException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SyncClipboardConfigUpgradeException($"Cannot parse configuration file '{configPath}'.", exception);
        }

        var originalVersion = ReadVersion(root);
        if (originalVersion > Env.SyncClipboardConfigVersion)
        {
            throw new SyncClipboardConfigUpgradeException(
                $"Configuration version {originalVersion} is newer than supported version {Env.SyncClipboardConfigVersion}.");
        }

        var version = originalVersion;
        while (version < Env.SyncClipboardConfigVersion)
        {
            if (!_migrations.TryGetValue(version, out var migration))
            {
                throw new SyncClipboardConfigUpgradeException(
                    $"Upgrading configuration from version {version} is not supported.");
            }

            try
            {
                migration.Migrate(root);
            }
            catch (SyncClipboardConfigUpgradeException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new SyncClipboardConfigUpgradeException(
                    $"Failed to upgrade configuration from version {version} to {migration.ToVersion}.",
                    exception);
            }

            version = migration.ToVersion;
            root[VersionPropertyName] = version;
        }

        ValidateCurrentConfig(root);

        if (version != originalVersion)
        {
            var backupPath = CreateBackup(configPath, originalVersion);
            PruneBackups(configPath, backupPath);
            AtomicWrite(configPath, root);
        }
    }

    private static int ReadVersion(JsonObject root)
    {
        if (root[VersionPropertyName] is null)
        {
            return 0;
        }

        if (root[VersionPropertyName] is not JsonValue value
            || !value.TryGetValue<int>(out var version)
            || version < 0)
        {
            throw new SyncClipboardConfigUpgradeException($"{VersionPropertyName} must be a non-negative integer.");
        }

        return version;
    }

    private static void ValidateCurrentConfig(JsonObject root)
    {
        if (ReadVersion(root) != Env.SyncClipboardConfigVersion)
        {
            throw new SyncClipboardConfigUpgradeException("Configuration migration did not reach the current version.");
        }

        var fileFilterNode = root["FileFilter"];
        if (fileFilterNode is null)
        {
            return;
        }

        var config = DeserializeFileFilter(fileFilterNode);
        ValidateFileFilterMode(config.FileFilterMode);
        ValidateFileFilterRules(config);
    }

    private static FileFilterConfig DeserializeFileFilter(JsonNode fileFilterNode)
    {
        try
        {
            return fileFilterNode.Deserialize<FileFilterConfig>()
                ?? throw new SyncClipboardConfigUpgradeException("FileFilter cannot be deserialized.");
        }
        catch (SyncClipboardConfigUpgradeException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new SyncClipboardConfigUpgradeException("FileFilter is invalid.", exception);
        }
    }

    private static void ValidateFileFilterMode(string filterMode)
    {
        if (filterMode is not ("" or "BlackList" or "WhiteList"))
        {
            throw new SyncClipboardConfigUpgradeException(
                $"FileFilter contains an unsupported filter mode '{filterMode}'.");
        }
    }

    private static void ValidateFileFilterRules(FileFilterConfig config)
    {
        if (config.WhiteList is null || config.BlackList is null)
        {
            throw new SyncClipboardConfigUpgradeException("FileFilter lists cannot be null.");
        }

        foreach (var rule in config.WhiteList.Concat(config.BlackList))
        {
            ValidateFileFilterRule(rule);
        }
    }

    private static void ValidateFileFilterRule(FileFilterRule? rule)
    {
        if (rule is null)
        {
            throw new SyncClipboardConfigUpgradeException("FileFilter cannot contain a null rule.");
        }

        if (!FileFilterHelper.TryValidateRule(rule, out var error))
        {
            throw new SyncClipboardConfigUpgradeException($"FileFilter contains an invalid rule: {error}");
        }
    }

    private static FileStream AcquireMigrationLock(string configPath)
    {
        try
        {
            return new FileStream(
                configPath + ".upgrade.lock",
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (Exception exception)
        {
            throw new SyncClipboardConfigUpgradeException("Cannot acquire the configuration upgrade lock.", exception);
        }
    }

    private static string CreateBackup(string configPath, int sourceVersion)
    {
        try
        {
            var backupDirectory = GetBackupDirectory(configPath);
            Directory.CreateDirectory(backupDirectory);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss.fffffff'Z'");
            var backupPath = Path.Combine(
                backupDirectory,
                $"{Path.GetFileNameWithoutExtension(configPath)}.v{sourceVersion}.{timestamp}.{Environment.ProcessId}.{Guid.NewGuid():N}.json");
            File.Copy(configPath, backupPath, overwrite: false);
            return backupPath;
        }
        catch (Exception exception)
        {
            throw new SyncClipboardConfigUpgradeException("Cannot back up SyncClipboard.json.", exception);
        }
    }

    private static string GetBackupDirectory(string configPath) => Path.Combine(
        Path.GetDirectoryName(configPath)!,
        "config_backup");

    private static void PruneBackups(string configPath, string backupToPreserve)
    {
        try
        {
            var backupDirectory = GetBackupDirectory(configPath);
            if (!Directory.Exists(backupDirectory))
            {
                return;
            }

            var files = Directory
                .EnumerateFiles(backupDirectory, $"{Path.GetFileNameWithoutExtension(configPath)}.v*.json")
                .OrderByDescending(file => string.Equals(file, backupToPreserve, StringComparison.Ordinal))
                .ThenByDescending(File.GetCreationTimeUtc)
                .Skip(BackupRetentionCount);

            foreach (var file in files)
            {
                File.Delete(file);
            }
        }
        catch
        {
            // Backup cleanup must not prevent startup or mask an upgrade validation failure.
        }
    }

    private static void AtomicWrite(string configPath, JsonObject root)
    {
        var tempPath = configPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var json = root.ToJsonString(JsonOptions);
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, configPath, overwrite: true);
        }
        catch (Exception exception)
        {
            throw new SyncClipboardConfigUpgradeException("Cannot write the upgraded configuration atomically.", exception);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Preserve the original upgrade exception if temporary-file cleanup also fails.
                }
            }
        }
    }
}
