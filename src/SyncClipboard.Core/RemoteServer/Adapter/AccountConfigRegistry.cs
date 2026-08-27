using SyncClipboard.Core.Attributes;
using SyncClipboard.Core.Commons;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Reflection;

namespace SyncClipboard.Core.RemoteServer.Adapter;

public sealed record AdapterConfigRegistration(
    string TypeName,
    Type ConfigType,
    int Priority);

public static class AccountConfigRegistry
{
    public static IReadOnlyList<AdapterConfigRegistration> Configurations { get; } = ScanConfigurations();

    public static void EnsureInitialized() => _ = Configurations.Count;

    private static readonly FrozenDictionary<string, AdapterConfigRegistration> ConfigurationsByName =
        Configurations.ToFrozenDictionary(config => config.TypeName);

    private static readonly FrozenDictionary<Type, AdapterConfigRegistration> ConfigurationsByType =
        Configurations.ToFrozenDictionary(config => config.ConfigType);

    public static AdapterConfigRegistration? GetRegistration(string typeName) =>
        ConfigurationsByName.GetValueOrDefault(typeName);

    public static AdapterConfigRegistration GetRegistration(Type configType) =>
        ConfigurationsByType.TryGetValue(configType, out var config)
            ? config
            : throw new ArgumentException($"Adapter configuration type '{configType}' is not registered.", nameof(configType));

    private static ReadOnlyCollection<AdapterConfigRegistration> ScanConfigurations()
    {
        var registrations = typeof(IAdapterConfig)
            .Assembly
            .GetTypes()
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<AccountConfigTypeAttribute>()))
            .Where(item => item.Attribute is not null)
            .Select(item => CreateRegistration(item.Type, item.Attribute!))
            .OrderBy(config => config.Priority)
            .ThenBy(config => config.TypeName, StringComparer.Ordinal)
            .ToArray();

        var duplicateName = registrations
            .GroupBy(config => config.TypeName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateName is not null)
        {
            var configTypes = string.Join(", ", duplicateName.Select(config => config.ConfigType));
            throw new ConfigRegistrationException(
                $"Account configuration key '{duplicateName.Key}' is registered more than once: {configTypes}.");
        }

        return Array.AsReadOnly(registrations);
    }

    private static AdapterConfigRegistration CreateRegistration(
        Type configType,
        AccountConfigTypeAttribute attribute)
    {
        if (!typeof(IAdapterConfig).IsAssignableFrom(configType))
        {
            throw new InvalidOperationException(
                $"Account configuration type '{configType}' must implement {nameof(IAdapterConfig)}.");
        }

        if (string.IsNullOrWhiteSpace(attribute.Name))
        {
            throw new InvalidOperationException($"Account configuration type '{configType}' must have a name.");
        }

        return new AdapterConfigRegistration(attribute.Name, configType, attribute.Priority);
    }
}
