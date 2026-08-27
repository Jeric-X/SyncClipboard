using SyncClipboard.Shared.Attributes;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Reflection;

namespace SyncClipboard.Core.Commons;

public sealed record ConfigRegistration(
    string Key,
    Type ConfigType,
    ConfigStorage Storage);

public static class SyncClipboardConfigRegistry
{
    private sealed record ConfigRegistryData(
        ReadOnlyCollection<ConfigRegistration> Configurations,
        FrozenDictionary<Type, string> BaseKeysByType);

    private static readonly ConfigRegistryData Registry = ScanConfigurations();

    public static IReadOnlyList<ConfigRegistration> Configurations => Registry.Configurations;

    public static void EnsureInitialized() => _ = Registry.Configurations;

    public static string GetDefaultKey(Type configType) =>
        Registry.BaseKeysByType.TryGetValue(configType, out var key)
            ? key
            : throw new ArgumentException($"Configuration type '{configType}' is not registered.", nameof(configType));

    public static string GetDefaultKey<T>() => GetDefaultKey(typeof(T));

    private static ConfigRegistryData ScanConfigurations()
    {
        var configTypes = new[]
            {
                typeof(SyncClipboardConfigRegistry).Assembly,
                typeof(ConfigKeyAttribute).Assembly,
            }
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .ToArray();

        var baseRegistrations = configTypes
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<ConfigKeyAttribute>()))
            .Where(item => item.Attribute is not null)
            .Select(item => CreateRegistration(
                item.Type,
                item.Attribute!.Key,
                item.Attribute.Storage))
            .ToArray();

        var optionalRegistrations = configTypes
            .SelectMany(type => type
                .GetCustomAttributes<OptionalConfigKeyAttribute>()
                .Select(attribute => CreateRegistration(type, attribute.Key, attribute.Storage)))
            .ToArray();

        var registrations = baseRegistrations
            .Concat(optionalRegistrations)
            .OrderBy(config => config.Storage)
            .ThenBy(config => config.Key, StringComparer.Ordinal)
            .ToArray();

        var duplicateKey = registrations
            .GroupBy(config => config.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateKey is not null)
        {
            var registrationsDescription = string.Join(
                ", ",
                duplicateKey.Select(config => $"{config.ConfigType} ({config.Storage})"));
            throw new ConfigRegistrationException(
                $"Configuration key '{duplicateKey.Key}' is registered more than once: {registrationsDescription}.");
        }

        return new ConfigRegistryData(
            Array.AsReadOnly(registrations),
            baseRegistrations.ToFrozenDictionary(config => config.ConfigType, config => config.Key));
    }

    private static ConfigRegistration CreateRegistration(
        Type configType,
        string key,
        ConfigStorage storage)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"Configuration type '{configType}' must have a key.");
        }

        return new ConfigRegistration(key, configType, storage);
    }
}
