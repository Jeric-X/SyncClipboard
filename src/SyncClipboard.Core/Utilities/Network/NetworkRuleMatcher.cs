using System.Net;
using System.Net.Sockets;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.Network;

public static class NetworkRuleMatcher
{
    public static NetworkRuleMatchResult? Match(
        IEnumerable<NetworkAccountSwitchRule> rules,
        NetworkContextSnapshot snapshot)
    {
        foreach (var rule in rules)
        {
            if (!rule.Enabled || !HasConditions(rule))
            {
                continue;
            }

            var interfaces = GetEligibleInterfaces(rule, snapshot).ToList();
            if (interfaces.Count == 0)
            {
                continue;
            }

            if (rule.MatchMode == NetworkRuleMatchMode.All)
            {
                var matched = interfaces.FirstOrDefault(item => MatchesAll(rule, item));
                if (matched is not null)
                {
                    return new NetworkRuleMatchResult(rule, matched);
                }
            }
            else
            {
                var matched = interfaces.FirstOrDefault(item => MatchesAny(rule, item));
                if (matched is not null)
                {
                    return new NetworkRuleMatchResult(rule, matched);
                }
            }
        }

        return null;
    }

    public static bool HasConditions(NetworkAccountSwitchRule rule) =>
        rule.WifiSsids.Any(value => !string.IsNullOrWhiteSpace(value))
        || rule.IpRanges.Any(value => TryParseRange(value, out _));

    public static bool TryNormalizeRange(string value, out string normalized)
    {
        normalized = string.Empty;
        if (!TryParseRange(value, out var range))
        {
            return false;
        }

        normalized = $"{range.Network}/{range.PrefixLength}";
        return true;
    }

    public static string RemoveRangeComment(string value)
    {
        var commentIndex = value.IndexOf('#');
        return (commentIndex >= 0 ? value[..commentIndex] : value).Trim();
    }

    public static bool IsAddressAllowed(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6Multicast)
        {
            return false;
        }

        var normalized = NormalizeAddress(address);
        if (normalized.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = normalized.GetAddressBytes();
            return !(bytes[0] == 169 && bytes[1] == 254)
                && bytes[0] is < 224 or > 239
                && !normalized.Equals(IPAddress.Any);
        }

        return !normalized.Equals(IPAddress.IPv6Any);
    }

    /// <summary>
    /// 判断地址是否可作为有效的默认网关。与 <see cref="IsAddressAllowed"/> 不同，
    /// IPv6 链路本地地址（fe80::/10）被视为有效网关，因为 IPv6 默认路由器通常使用链路本地地址。
    /// </summary>
    public static bool IsValidGateway(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6Multicast)
        {
            return false;
        }

        var normalized = NormalizeAddress(address);
        if (normalized.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = normalized.GetAddressBytes();
            return !(bytes[0] == 169 && bytes[1] == 254)
                && bytes[0] is < 224 or > 239
                && !normalized.Equals(IPAddress.Any);
        }

        return !normalized.Equals(IPAddress.IPv6Any);
    }

    private static IEnumerable<NetworkInterfaceSnapshot> GetEligibleInterfaces(NetworkAccountSwitchRule rule, NetworkContextSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(rule.NetworkInterfaceId))
        {
            return snapshot.Interfaces.Where(item =>
                string.Equals(item.Id, rule.NetworkInterfaceId, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(rule.NetworkInterfaceName)
                    && string.Equals(item.Name, rule.NetworkInterfaceName, StringComparison.Ordinal)));
        }

        return snapshot.Interfaces.Where(item => item.HasDefaultGateway);
    }

    private static bool MatchesAny(NetworkAccountSwitchRule rule, NetworkInterfaceSnapshot networkInterface) =>
        MatchesWifi(rule, networkInterface) || MatchesIp(rule, networkInterface);

    private static bool MatchesAll(NetworkAccountSwitchRule rule, NetworkInterfaceSnapshot networkInterface)
    {
        var hasWifi = rule.WifiSsids.Any(value => !string.IsNullOrWhiteSpace(value));
        var hasIp = rule.IpRanges.Any(value => TryParseRange(value, out _));

        return (!hasWifi || MatchesWifi(rule, networkInterface))
            && (!hasIp || MatchesIp(rule, networkInterface));
    }

    private static bool MatchesWifi(NetworkAccountSwitchRule rule, NetworkInterfaceSnapshot networkInterface) =>
        networkInterface.WifiSsid is not null
        && rule.WifiSsids.Any(ssid => string.Equals(ssid.Trim(), networkInterface.WifiSsid, StringComparison.Ordinal));

    private static bool MatchesIp(NetworkAccountSwitchRule rule, NetworkInterfaceSnapshot networkInterface)
    {
        var ranges = rule.IpRanges
            .Select(value => TryParseRange(value, out var range) ? range : null)
            .Where(range => range is not null)
            .Cast<IpRange>()
            .ToList();

        return networkInterface.Addresses.Any(address => ranges.Any(range => range.Contains(address)));
    }

    private static bool TryParseRange(string value, out IpRange range)
    {
        range = null!;
        value = RemoveRangeComment(value);
        if (value.Length == 0)
        {
            return false;
        }

        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (!IPAddress.TryParse(parts[0], out var address))
        {
            return false;
        }

        address = NormalizeAddress(address);
        var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefix = maxPrefix;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > maxPrefix))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        ApplyMask(bytes, prefix);
        range = new IpRange(new IPAddress(bytes), prefix);
        return true;
    }

    private static IPAddress NormalizeAddress(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : new IPAddress(address.GetAddressBytes());

    private static void ApplyMask(byte[] bytes, int prefix)
    {
        var fullBytes = prefix / 8;
        var remainingBits = prefix % 8;
        if (remainingBits > 0)
        {
            bytes[fullBytes] &= (byte)(0xff << (8 - remainingBits));
            fullBytes++;
        }

        Array.Clear(bytes, fullBytes, bytes.Length - fullBytes);
    }

    private sealed record IpRange(IPAddress Network, int PrefixLength)
    {
        public bool Contains(IPAddress address)
        {
            address = NormalizeAddress(address);
            if (address.AddressFamily != Network.AddressFamily)
            {
                return false;
            }

            var bytes = address.GetAddressBytes();
            ApplyMask(bytes, PrefixLength);
            return bytes.SequenceEqual(Network.GetAddressBytes());
        }
    }
}
