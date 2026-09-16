using System.Net;
using System.Collections.Concurrent;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.Utilities.Network;

namespace SyncClipboard.Test;

[TestClass]
public class NetworkRuleMatcherTests
{
    private static readonly AccountConfig AccountOne = new() { AccountId = "1", AccountType = "WebDAV" };
    private static readonly AccountConfig AccountTwo = new() { AccountId = "2", AccountType = "WebDAV" };
    private static readonly int[] expected = [2];

    [TestMethod]
    [DataRow(false, false, false, false, false)]
    [DataRow(false, false, true, true, true)]
    [DataRow(true, false, false, true, false)]
    [DataRow(true, true, false, true, true)]
    [DataRow(true, false, true, true, true)]
    public void NetworkMonitoringDemand_UsesSwitchRulesAndVisibleStatusListeners(
        bool autoSwitchEnabled,
        bool hasEnabledWifiRules,
        bool hasStatusListeners,
        bool expectedListening,
        bool expectedWifiPolling)
    {
        var demand = NetworkMonitoringDemand.Calculate(
            autoSwitchEnabled,
            hasEnabledWifiRules,
            hasStatusListeners);

        Assert.AreEqual(expectedListening, demand.ListenForNetworkChanges);
        Assert.AreEqual(expectedWifiPolling, demand.PollWifi);
    }

    [TestMethod]
    public void NormalizeRange_NormalizesExactAndCidrAddresses()
    {
        Assert.IsTrue(NetworkRuleMatcher.TryNormalizeRange("192.168.10.42", out var exact));
        Assert.AreEqual("192.168.10.42/32", exact);

        Assert.IsTrue(NetworkRuleMatcher.TryNormalizeRange("192.168.10.42/24", out var subnet));
        Assert.AreEqual("192.168.10.0/24", subnet);

        Assert.IsTrue(NetworkRuleMatcher.TryNormalizeRange("2001:db8::1234/64", out var ipv6));
        Assert.AreEqual("2001:db8::/64", ipv6);

        Assert.IsTrue(NetworkRuleMatcher.TryNormalizeRange(" 192.168.10.42/24  # Wi-Fi ", out var commented));
        Assert.AreEqual("192.168.10.0/24", commented);
    }

    [TestMethod]
    [DataRow("192.168.1.1/33")]
    [DataRow("2001:db8::1/129")]
    [DataRow("not-an-address")]
    [DataRow("")]
    public void NormalizeRange_RejectsInvalidValues(string value)
    {
        Assert.IsFalse(NetworkRuleMatcher.TryNormalizeRange(value, out _));
    }

    [TestMethod]
    public void Match_UsesFirstMatchingEnabledRule()
    {
        var snapshot = Snapshot(Interface("wifi", true, "Home", "192.168.1.20"));
        var first = Rule("first", AccountOne, ["Home"], []);
        var second = Rule("second", AccountTwo, [], ["192.168.1.0/24"]);

        var result = NetworkRuleMatcher.Match([first, second], snapshot);

        Assert.IsNotNull(result);
        Assert.AreEqual("first", result.Rule.Name);
        Assert.AreEqual(AccountOne, result.Rule.TargetAccount);
    }

    [TestMethod]
    public void Match_AnyModeMatchesEitherConditionGroup()
    {
        var snapshot = Snapshot(Interface("wifi", true, "Different", "10.10.5.8"));
        var rule = Rule("office", AccountOne, ["Office"], ["10.10.0.0/16"]);
        rule.MatchMode = NetworkRuleMatchMode.Any;

        Assert.IsNotNull(NetworkRuleMatcher.Match([rule], snapshot));
    }

    [TestMethod]
    public void Match_AllModeRequiresSameInterfaceToMatchBothGroups()
    {
        var snapshot = Snapshot(
            Interface("wifi", true, "Office", "192.168.1.20"),
            Interface("vpn", true, null, "10.10.5.8"));
        var rule = Rule("office", AccountOne, ["Office"], ["10.10.0.0/16"]);
        rule.MatchMode = NetworkRuleMatchMode.All;

        Assert.IsNull(NetworkRuleMatcher.Match([rule], snapshot));

        snapshot = Snapshot(Interface("wifi", true, "Office", "10.10.5.8"));
        Assert.IsNotNull(NetworkRuleMatcher.Match([rule], snapshot));
    }

    [TestMethod]
    public void Match_UnboundRuleOnlyUsesDefaultGatewayInterfaces()
    {
        var snapshot = Snapshot(Interface("docker", false, null, "172.18.0.2"));
        var rule = Rule("container", AccountOne, [], ["172.18.0.0/16"]);

        Assert.IsNull(NetworkRuleMatcher.Match([rule], snapshot));

        rule.NetworkInterfaceId = "docker";
        Assert.IsNotNull(NetworkRuleMatcher.Match([rule], snapshot));
    }

    [TestMethod]
    public void Evaluator_RemovesAccountWhenFirstMatchedRuleTargetsMissingAccount()
    {
        var snapshot = Snapshot(Interface("wifi", true, "Home", "192.168.1.20"));
        var missing = Rule("missing", AccountOne, ["Home"], []);
        var valid = Rule("valid", AccountTwo, ["Home"], []);
        var config = new NetworkAccountSwitchConfig
        {
            Rules = [missing, valid],
        };

        var decision = NetworkAccountSwitchEvaluator.Evaluate(config, snapshot, account => account == AccountTwo);

        Assert.AreEqual(NetworkAccountSwitchDecisionKind.RemoveSyncAccount, decision.Kind);
        Assert.AreEqual("missing", decision.Match?.Rule.Name);

        missing.TargetAccount = new();
        decision = NetworkAccountSwitchEvaluator.Evaluate(config, snapshot, account => account == AccountTwo);
        Assert.AreEqual(NetworkAccountSwitchDecisionKind.RemoveSyncAccount, decision.Kind);
    }

    [TestMethod]
    public void Match_IsCaseSensitiveForWifiNames()
    {
        var snapshot = Snapshot(Interface("wifi", true, "home", "192.168.1.20"));
        var rule = Rule("home", AccountOne, ["Home"], []);

        Assert.IsNull(NetworkRuleMatcher.Match([rule], snapshot));
    }

    [TestMethod]
    public void Match_HandlesIpv4AndIpv6CidrBoundaries()
    {
        var ipv4Rule = Rule("v4", AccountOne, [], ["10.0.0.0/24"]);
        Assert.IsNotNull(NetworkRuleMatcher.Match([ipv4Rule], Snapshot(Interface("ethernet", true, null, "10.0.0.0"))));
        Assert.IsNotNull(NetworkRuleMatcher.Match([ipv4Rule], Snapshot(Interface("ethernet", true, null, "10.0.0.255"))));
        Assert.IsNull(NetworkRuleMatcher.Match([ipv4Rule], Snapshot(Interface("ethernet", true, null, "10.0.1.0"))));

        var ipv6Rule = Rule("v6", AccountOne, [], ["2001:db8::/126"]);
        Assert.IsNotNull(NetworkRuleMatcher.Match([ipv6Rule], Snapshot(Interface("ethernet", true, null, "2001:db8::3"))));
        Assert.IsNull(NetworkRuleMatcher.Match([ipv6Rule], Snapshot(Interface("ethernet", true, null, "2001:db8::4"))));
    }

    [TestMethod]
    public void Match_IgnoresInlineAndCommentOnlyIpText()
    {
        var snapshot = Snapshot(Interface("wifi", true, null, "192.168.1.20"));
        var rule = Rule("home", AccountOne, [], ["# home network", " 192.168.1.0/24 # Wi-Fi "]);

        Assert.IsNotNull(NetworkRuleMatcher.Match([rule], snapshot));
        Assert.AreEqual(string.Empty, NetworkRuleMatcher.RemoveRangeComment("  # comment only "));
    }

    [TestMethod]
    public async Task Evaluator_UsesFakeProviderAndAppliesFallbackBehavior()
    {
        var provider = new FakeNetworkContextProvider(Snapshot(Interface("wifi", true, "Away", "10.0.0.2")));
        var config = new NetworkAccountSwitchConfig
        {
            Enabled = true,
            NoMatchAction = NetworkNoMatchAction.SwitchToDefaultAccount,
            DefaultAccount = AccountTwo,
            Rules = [Rule("home", AccountOne, ["Home"], [])],
        };

        var snapshot = await provider.GetCurrentAsync(false, TestContext.CancellationTokenSource.Token);
        var decision = NetworkAccountSwitchEvaluator.Evaluate(config, snapshot, account => account == AccountTwo);
        Assert.AreEqual(NetworkAccountSwitchDecisionKind.SwitchAccount, decision.Kind);
        Assert.AreEqual(AccountTwo, decision.TargetAccount);

        config.DefaultAccount = AccountOne;
        decision = NetworkAccountSwitchEvaluator.Evaluate(config, snapshot, account => account == AccountTwo);
        Assert.AreEqual(NetworkAccountSwitchDecisionKind.RemoveSyncAccount, decision.Kind);

        config.DefaultAccount = new();
        decision = NetworkAccountSwitchEvaluator.Evaluate(config, snapshot, _ => false);
        Assert.AreEqual(NetworkAccountSwitchDecisionKind.RemoveSyncAccount, decision.Kind);

        config.NoMatchAction = NetworkNoMatchAction.RemoveSyncAccount;
        decision = NetworkAccountSwitchEvaluator.Evaluate(config, snapshot, _ => false);
        Assert.AreEqual(NetworkAccountSwitchDecisionKind.RemoveSyncAccount, decision.Kind);
    }

    [TestMethod]
    public void RuntimeState_ManualOverrideResetsForNetworkOrConfigChangesAndDeduplicatesResults()
    {
        var state = new NetworkAccountSwitchRuntimeState();
        state.OnManualSelection();
        Assert.IsTrue(state.ManualOverride);
        Assert.IsTrue(state.OnNetworkChanged());
        Assert.IsFalse(state.ManualOverride);

        Assert.IsTrue(state.ShouldEvaluate("network-a", false));
        Assert.IsFalse(state.ShouldEvaluate("network-a", false));
        Assert.IsTrue(state.ShouldEvaluate("network-a", true));

        state.OnManualSelection();
        state.OnConfigurationChanged();
        Assert.IsFalse(state.ManualOverride);
        Assert.IsTrue(state.ShouldEvaluate("network-a", false));
    }

    [TestMethod]
    public async Task Debouncer_CancelsOlderWorkAndRunsLatestSerially()
    {
        using var debouncer = new AsyncLatestWinsDebouncer();
        var values = new ConcurrentQueue<int>();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = debouncer.ScheduleAsync(async token =>
        {
            firstStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            values.Enqueue(1);
        }, TimeSpan.Zero);

        await firstStarted.Task;
        var second = debouncer.ScheduleAsync(_ =>
        {
            values.Enqueue(2);
            return Task.CompletedTask;
        }, TimeSpan.Zero);

        await Task.WhenAll(first, second);
        CollectionAssert.AreEqual(expected, values.ToArray());
    }

    [TestMethod]
    public void ConfigCleaner_RemovesRulesAndRestoresRemoveAccountFallback()
    {
        var config = new NetworkAccountSwitchConfig
        {
            NoMatchAction = NetworkNoMatchAction.SwitchToDefaultAccount,
            DefaultAccount = AccountOne,
            Rules = [Rule("one", AccountOne, ["Home"], []), Rule("two", AccountTwo, ["Office"], [])],
        };

        var result = NetworkAccountSwitchConfigCleaner.RemoveAccount(config, AccountOne);

        Assert.AreEqual(1, result.RemovedRuleCount);
        Assert.IsTrue(result.RemovedDefaultAccount);
        Assert.AreEqual(NetworkNoMatchAction.RemoveSyncAccount, result.Config.NoMatchAction);
        Assert.AreEqual(AccountTwo, result.Config.Rules.Single().TargetAccount);
    }

    [TestMethod]
    public void SnapshotFingerprintTracksGatewayAndWifiCapabilityChanges()
    {
        var withoutGateway = Snapshot(Interface("wifi", false, "Home", "192.168.1.2"));
        var withGateway = Snapshot(Interface("wifi", true, "Home", "192.168.1.2"));
        var unsupported = withGateway with { WifiStatus = WifiAccessStatus.Unsupported };

        Assert.AreNotEqual(withoutGateway.Fingerprint, withGateway.Fingerprint);
        Assert.AreNotEqual(withGateway.Fingerprint, unsupported.Fingerprint);
    }

    [TestMethod]
    public void AddressFilterRejectsLoopbackAndLinkLocalAddresses()
    {
        Assert.IsFalse(NetworkRuleMatcher.IsAddressAllowed(IPAddress.Loopback));
        Assert.IsFalse(NetworkRuleMatcher.IsAddressAllowed(IPAddress.Parse("169.254.1.2")));
        Assert.IsFalse(NetworkRuleMatcher.IsAddressAllowed(IPAddress.Parse("fe80::1")));
        Assert.IsTrue(NetworkRuleMatcher.IsAddressAllowed(IPAddress.Parse("192.168.1.2")));
        Assert.IsTrue(NetworkRuleMatcher.IsAddressAllowed(IPAddress.Parse("2001:db8::1")));
    }

    [TestMethod]
    public async Task EmptyRemoteClipboardServerIsInertAndRejectsSyncOperations()
    {
        var server = EmptyRemoteClipboardServer.Instance;

        Assert.IsFalse(await server.TestConnectionAsync(TestContext.CancellationTokenSource.Token));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => server.GetProfileAsync(TestContext.CancellationTokenSource.Token));
    }

    private static NetworkAccountSwitchRule Rule(
        string name,
        AccountConfig account,
        List<string> ssids,
        List<string> ranges) => new()
        {
            Name = name,
            TargetAccount = account,
            WifiSsids = ssids,
            IpRanges = ranges,
        };

    private static NetworkInterfaceSnapshot Interface(
        string id,
        bool hasGateway,
        string? ssid,
        params string[] addresses) => new()
        {
            Id = id,
            Name = id,
            HasDefaultGateway = hasGateway,
            WifiSsid = ssid,
            Addresses = addresses.Select(IPAddress.Parse).ToList(),
        };

    private static NetworkContextSnapshot Snapshot(params NetworkInterfaceSnapshot[] interfaces) => new()
    {
        Interfaces = interfaces,
        WifiStatus = WifiAccessStatus.Available,
    };

    private sealed class FakeNetworkContextProvider(NetworkContextSnapshot snapshot) : INetworkContextProvider
    {
        public event EventHandler? NetworkChanged { add { } remove { } }
        public bool CanOpenWifiSettings => false;
        public Task<NetworkContextSnapshot> GetCurrentAsync(bool requestWifiAccess, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
        public void OpenWifiSettings() { }
    }

    public TestContext TestContext { get; set; } = null!;
}
