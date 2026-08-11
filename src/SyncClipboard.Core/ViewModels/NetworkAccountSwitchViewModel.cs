using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.Utilities.Network;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Sockets;

namespace SyncClipboard.Core.ViewModels;

public sealed record NetworkInterfaceChoice(string Id, string Name, string DisplayName);
public sealed record NetworkOption<T>(T Value, string DisplayName) where T : struct, Enum
{
    public override string ToString() => DisplayName;
}

public partial class NetworkRuleEditor : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private DisplayedAccountConfig? targetAccount;
    [ObservableProperty] private NetworkOption<NetworkRuleMatchMode> selectedMatchMode = NetworkAccountSwitchViewModel.MatchModes[0];
    [ObservableProperty] private NetworkInterfaceChoice? selectedInterface;
    [ObservableProperty] private string wifiText = string.Empty;
    [ObservableProperty] private string ipText = string.Empty;

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Strings.AddRule : Name;
}

public partial class NetworkAccountSwitchViewModel : ObservableObject
{
    public static readonly NetworkOption<NetworkNoMatchAction>[] NoMatchActions =
    [
        new(NetworkNoMatchAction.KeepCurrent, Strings.KeepCurrentAccount),
        new(NetworkNoMatchAction.SwitchToDefaultAccount, Strings.SwitchToDefaultAccount),
        new(NetworkNoMatchAction.RemoveSyncAccount, Strings.RemoveSyncAccount),
    ];
    public static readonly NetworkOption<NetworkRuleMatchMode>[] MatchModes =
    [
        new(NetworkRuleMatchMode.Any, Strings.MatchAny),
        new(NetworkRuleMatchMode.All, Strings.MatchAll),
    ];

    private readonly ConfigManager _configManager;
    private readonly AccountManager _accountManager;
    private readonly NetworkAccountSwitchService _service;
    private readonly IMainWindowDialog _dialog;
    private readonly IThreadDispatcher _dispatcher;
    private bool _autoSaveEnabled;
    private bool _active;

    [ObservableProperty] private bool enabled;
    [ObservableProperty] private bool notifyOnChange = true;
    [ObservableProperty] private NetworkOption<NetworkNoMatchAction> selectedNoMatchAction = NoMatchActions[0];
    [ObservableProperty] private DisplayedAccountConfig? defaultAccount;
    [ObservableProperty] private NetworkRuleEditor? selectedRule;
    [ObservableProperty] private string statusText = Strings.Disabled;
    [ObservableProperty] private string wifiPermissionStatusText = Strings.WifiNotRequested;
    [ObservableProperty] private string wifiPermissionButtonText = Strings.RequestPermission;
    [ObservableProperty] private bool canRequestWifiAccess = true;
    [ObservableProperty] private bool canOpenWifiSettings;

    public ObservableCollection<DisplayedAccountConfig> Accounts { get; } = [];
    public ObservableCollection<NetworkInterfaceChoice> Interfaces { get; } = [];
    public ObservableCollection<NetworkRuleEditor> Rules { get; } = [];

    public NetworkNoMatchAction NoMatchAction => SelectedNoMatchAction.Value;
    public bool ShowDefaultAccount => SelectedNoMatchAction.Value == NetworkNoMatchAction.SwitchToDefaultAccount;

    partial void OnSelectedNoMatchActionChanged(NetworkOption<NetworkNoMatchAction> value)
    {
        OnPropertyChanged(nameof(NoMatchAction));
        OnPropertyChanged(nameof(ShowDefaultAccount));
        SaveImmediately();
    }

    partial void OnEnabledChanged(bool value) => SaveImmediately();
    partial void OnNotifyOnChangeChanged(bool value) => SaveImmediately();
    partial void OnDefaultAccountChanged(DisplayedAccountConfig? value) => SaveImmediately();

    public NetworkAccountSwitchViewModel(
        ConfigManager configManager,
        AccountManager accountManager,
        NetworkAccountSwitchService service,
        IMainWindowDialog dialog,
        IThreadDispatcher dispatcher)
    {
        _configManager = configManager;
        _accountManager = accountManager;
        _service = service;
        _dialog = dialog;
        _dispatcher = dispatcher;

        LoadAccounts();
        LoadConfig(configManager.GetConfig<NetworkAccountSwitchConfig>());
        _autoSaveEnabled = true;
        UpdateRuntimeState();
    }

    public void Activate()
    {
        if (_active) return;
        _active = true;
        _autoSaveEnabled = false;
        LoadAccounts();
        LoadConfig(_configManager.GetConfig<NetworkAccountSwitchConfig>());
        _autoSaveEnabled = true;
        _service.StatusChanged += OnServiceStatusChanged;
        _service.AddNetworkMonitoringDemand();
        _accountManager.SavedAccountsChanged += OnSavedAccountsChanged;
        UpdateRuntimeState();
        _ = RefreshAsync();
    }

    public void Deactivate()
    {
        if (!_active) return;
        _active = false;
        _service.RemoveNetworkMonitoringDemand();
        _service.StatusChanged -= OnServiceStatusChanged;
        _accountManager.SavedAccountsChanged -= OnSavedAccountsChanged;
    }

    [RelayCommand]
    private void AddRule()
    {
        var rule = CreateRuleEditor();
        Rules.Add(rule);
        SelectedRule = rule;
    }

    public NetworkRuleEditor CreateRuleEditor() => new()
    {
        Name = $"{Strings.Rules} {Rules.Count + 1}",
        TargetAccount = Accounts.FirstOrDefault(),
        SelectedInterface = Interfaces.FirstOrDefault(),
    };

    public static NetworkRuleEditor CloneRuleEditor(NetworkRuleEditor source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Enabled = source.Enabled,
        TargetAccount = source.TargetAccount,
        SelectedMatchMode = source.SelectedMatchMode,
        SelectedInterface = source.SelectedInterface,
        WifiText = source.WifiText,
        IpText = source.IpText,
    };

    public void AddRuleEditor(NetworkRuleEditor editor)
    {
        SubscribeRule(editor);
        Rules.Add(editor);
        SelectedRule = editor;
        SaveImmediately();
    }

    public void UpdateRuleEditor(NetworkRuleEditor target, NetworkRuleEditor source)
    {
        UnsubscribeRule(target);
        try
        {
            target.Name = source.Name;
            target.Enabled = source.Enabled;
            target.TargetAccount = source.TargetAccount;
            target.SelectedMatchMode = source.SelectedMatchMode;
            target.SelectedInterface = source.SelectedInterface;
            target.WifiText = source.WifiText;
            target.IpText = source.IpText;
        }
        finally
        {
            SubscribeRule(target);
        }
        SelectedRule = target;
        SaveImmediately();
    }

    public string ValidateRuleEditor(
        NetworkRuleEditor editor,
        IReadOnlyCollection<NetworkInterfaceChoice>? availableInterfaces = null)
    {
        availableInterfaces ??= Interfaces;
        if (string.IsNullOrWhiteSpace(editor.Name)) return Strings.EnterRuleName;
        if (editor.TargetAccount is null) return Strings.SelectAccountFirst;

        if (FindMissingInterface(editor.SelectedInterface, availableInterfaces) is { } missingInterface)
        {
            return string.Format(Strings.MissingNetworkInterface, missingInterface.DisplayName);
        }

        var ssids = SplitLines(editor.WifiText);
        var (hasIpRange, ipError) = AnalyzeIpRanges(editor.IpText);
        if (ipError is not null) return ipError;
        if (ssids.Count == 0 && !hasIpRange) return Strings.AddConditionFirst;
        return string.Empty;
    }

    private static NetworkInterfaceChoice? FindMissingInterface(
        NetworkInterfaceChoice? selected,
        IReadOnlyCollection<NetworkInterfaceChoice> available)
    {
        if (selected is not { Id.Length: > 0 } iface) return null;
        return available.Any(item => string.Equals(item.Id, iface.Id, StringComparison.OrdinalIgnoreCase))
            ? null
            : iface;
    }

    private static (bool HasIpRange, string? Error) AnalyzeIpRanges(string ipText)
    {
        var hasIpRange = false;
        foreach (var value in SplitLines(ipText))
        {
            if (NetworkRuleMatcher.RemoveRangeComment(value).Length == 0) continue;
            if (!NetworkRuleMatcher.TryNormalizeRange(value, out _))
            {
                return (false, string.Format(Strings.InvalidIpRange, value));
            }
            hasIpRange = true;
        }
        return (hasIpRange, null);
    }

    public void UseCurrentWifi(NetworkRuleEditor editor)
    {
        foreach (var ssid in _service.Snapshot.Interfaces
            .Select(item => item.WifiSsid)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal))
        {
            editor.WifiText = AppendLine(editor.WifiText, ssid);
        }
    }

    public void UseCurrentIp(NetworkRuleEditor editor)
    {
        var selectedId = editor.SelectedInterface?.Id;
        var networkInterfaces = string.IsNullOrEmpty(selectedId)
            ? _service.Snapshot.Interfaces.Where(item => item.HasDefaultGateway)
            : _service.Snapshot.Interfaces.Where(item => item.Id == selectedId);

        foreach (var entry in networkInterfaces
            .SelectMany(item => item.Addresses
                .OrderBy(address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .Select(address => new { Address = address, InterfaceName = item.DisplayName }))
            .DistinctBy(item => item.Address))
        {
            editor.IpText = AppendLine(editor.IpText, $"{entry.Address} # {entry.InterfaceName}");
        }
    }

    [RelayCommand]
    private async Task DeleteRule()
    {
        if (SelectedRule is null) return;
        var confirmed = await _dialog.ShowConfirmationAsync(
            Strings.DeleteRule,
            string.Format(Strings.DeleteRuleConfirm, SelectedRule.Name));
        if (!confirmed) return;

        var rule = SelectedRule;
        var index = Rules.IndexOf(rule);
        Rules.Remove(rule);
        SelectedRule = Rules.Count == 0 ? null : Rules[Math.Clamp(index, 0, Rules.Count - 1)];
        UnsubscribeRule(rule);
        SaveImmediately();
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedRule is null) return;
        var index = Rules.IndexOf(SelectedRule);
        if (index > 0)
        {
            Rules.Move(index, index - 1);
            SaveImmediately();
        }
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedRule is null) return;
        var index = Rules.IndexOf(SelectedRule);
        if (index >= 0 && index < Rules.Count - 1)
        {
            Rules.Move(index, index + 1);
            SaveImmediately();
        }
    }

    [RelayCommand]
    private async Task RequestWifiAccess()
    {
        WifiPermissionStatusText = Strings.WifiRequesting;
        WifiPermissionButtonText = Strings.WifiRequesting;
        CanRequestWifiAccess = false;
        await _service.RefreshAsync(requestWifiAccess: true);
        var config = _configManager.GetConfig<NetworkAccountSwitchConfig>() with { WifiAccessRequested = true };
        _configManager.SetConfig(config);
        UpdateRuntimeState();
    }

    [RelayCommand]
    private void OpenWifiSettings() => _service.OpenWifiSettings();

    [RelayCommand]
    private void UseCurrentWifi()
    {
        if (SelectedRule is null) return;
        UseCurrentWifi(SelectedRule);
    }

    [RelayCommand]
    private void UseCurrentIp()
    {
        if (SelectedRule is null) return;
        UseCurrentIp(SelectedRule);
    }

    private void SaveImmediately()
    {
        if (_autoSaveEnabled) _configManager.SetConfig(BuildConfig());
    }

    private NetworkAccountSwitchConfig BuildConfig()
    {
        var rules = new List<NetworkAccountSwitchRule>();
        foreach (var editor in Rules)
        {
            var ssids = SplitLines(editor.WifiText);
            var ranges = new List<string>();
            foreach (var value in SplitLines(editor.IpText))
            {
                if (NetworkRuleMatcher.TryNormalizeRange(value, out var normalized))
                {
                    var commentIndex = value.IndexOf('#');
                    var comment = commentIndex >= 0 ? value[(commentIndex + 1)..].Trim() : string.Empty;
                    ranges.Add(comment.Length == 0 ? normalized : $"{normalized} # {comment}");
                }
                else
                {
                    ranges.Add(value.Trim());
                }
            }

            rules.Add(new()
            {
                Id = editor.Id,
                Name = editor.Name.Trim(),
                Enabled = editor.Enabled,
                TargetAccount = ToAccountConfig(editor.TargetAccount),
                MatchMode = editor.SelectedMatchMode.Value,
                NetworkInterfaceId = editor.SelectedInterface?.Id ?? string.Empty,
                NetworkInterfaceName = editor.SelectedInterface?.Name ?? string.Empty,
                WifiSsids = ssids,
                IpRanges = ranges.Distinct(StringComparer.Ordinal).ToList(),
            });
        }

        return new NetworkAccountSwitchConfig
        {
            Enabled = Enabled,
            NotifyOnChange = NotifyOnChange,
            WifiAccessRequested = _configManager.GetConfig<NetworkAccountSwitchConfig>().WifiAccessRequested,
            NoMatchAction = NoMatchAction,
            DefaultAccount = ToAccountConfig(DefaultAccount),
            Rules = rules,
        };
    }

    private void LoadConfig(NetworkAccountSwitchConfig config)
    {
        _autoSaveEnabled = false;
        Enabled = config.Enabled;
        NotifyOnChange = config.NotifyOnChange;
        SelectedNoMatchAction = NoMatchActions.First(item => item.Value == config.NoMatchAction);
        DefaultAccount = FindAccount(config.DefaultAccount);
        foreach (var editor in Rules) UnsubscribeRule(editor);
        Rules.Clear();
        foreach (var rule in config.Rules)
        {
            var editor = new NetworkRuleEditor
            {
                Id = rule.Id,
                Name = rule.Name,
                Enabled = rule.Enabled,
                TargetAccount = FindAccount(rule.TargetAccount),
                SelectedMatchMode = MatchModes.First(item => item.Value == rule.MatchMode),
                SelectedInterface = FindInterface(rule.NetworkInterfaceId, rule.NetworkInterfaceName),
                WifiText = string.Join(Environment.NewLine, rule.WifiSsids),
                IpText = string.Join(Environment.NewLine, rule.IpRanges),
            };
            SubscribeRule(editor);
            Rules.Add(editor);
        }
        SelectedRule = Rules.FirstOrDefault();
    }

    private void SubscribeRule(NetworkRuleEditor rule) => rule.PropertyChanged += OnRulePropertyChanged;
    private void UnsubscribeRule(NetworkRuleEditor rule) => rule.PropertyChanged -= OnRulePropertyChanged;

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NetworkRuleEditor.Enabled)) SaveImmediately();
    }

    private async Task RefreshAsync()
    {
        try
        {
            await _service.RefreshAsync();
            if (_active) await _dispatcher.RunOnMainThreadAsync(UpdateRuntimeState);
        }
        catch (OperationCanceledException) when (!_active) { }
    }

    private void UpdateRuntimeState()
    {
        StatusText = NetworkAccountSwitchStatusFormatter.Format(_service.Status);
        var wifiStatus = _service.Snapshot.WifiStatus;
        var wifiNames = _service.Snapshot.Interfaces
            .Select(item => item.WifiSsid)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var connectedWifi = string.Join(", ", wifiNames);

        CanOpenWifiSettings = _service.CanOpenWifiSettings
            && wifiStatus is WifiAccessStatus.Denied or WifiAccessStatus.Error;
        CanRequestWifiAccess = wifiStatus is WifiAccessStatus.NotRequested or WifiAccessStatus.Error;
        WifiPermissionButtonText = wifiStatus == WifiAccessStatus.Available
            ? Strings.WifiGranted
            : Strings.RequestPermission;
        WifiPermissionStatusText = wifiStatus switch
        {
            WifiAccessStatus.NotRequested => Strings.WifiNotRequested,
            WifiAccessStatus.Available when wifiNames.Length > 0 => string.Format(Strings.WifiGrantedWithName, connectedWifi),
            WifiAccessStatus.Available => Strings.WifiGrantedNoNetwork,
            WifiAccessStatus.Denied => Strings.WifiDenied,
            WifiAccessStatus.Unsupported => Strings.WifiUnsupported,
            WifiAccessStatus.Error => _service.Snapshot.WifiError ?? Strings.WifiUnsupported,
            _ => Strings.WifiUnsupported,
        };
        var selectedIds = Rules.ToDictionary(rule => rule.Id, rule => (rule.SelectedInterface?.Id, rule.SelectedInterface?.Name));
        Interfaces.Clear();
        Interfaces.Add(new(string.Empty, string.Empty, Strings.AutomaticInterface));
        foreach (var item in _service.Snapshot.Interfaces)
        {
            Interfaces.Add(new(item.Id, item.Name, item.DisplayName));
        }

        foreach (var rule in Rules)
        {
            var (Id, Name) = selectedIds[rule.Id];
            rule.SelectedInterface = FindInterface(Id, Name);
        }
    }

    private void OnServiceStatusChanged(object? sender, EventArgs e) => _ = _dispatcher.RunOnMainThreadAsync(() =>
    {
        if (_active) UpdateRuntimeState();
    });

    private void OnSavedAccountsChanged(IEnumerable<DisplayedAccountConfig> accounts) => _ = _dispatcher.RunOnMainThreadAsync(() =>
    {
        if (!_active) return;
        var selectedTargets = Rules.ToDictionary(rule => rule.Id, rule => ToAccountConfig(rule.TargetAccount));
        var defaultSelection = ToAccountConfig(DefaultAccount);
        LoadAccounts(accounts);
        DefaultAccount = FindAccount(defaultSelection);
        foreach (var rule in Rules)
        {
            rule.TargetAccount = FindAccount(selectedTargets[rule.Id]);
        }
    });

    private void LoadAccounts(IEnumerable<DisplayedAccountConfig>? accounts = null)
    {
        Accounts.Clear();
        foreach (var account in accounts ?? _accountManager.GetSavedAccounts()) Accounts.Add(account);
    }

    private DisplayedAccountConfig? FindAccount(AccountConfig account) => Accounts.FirstOrDefault(item =>
        item.AccountId == account.AccountId && item.AccountType == account.AccountType);

    private NetworkInterfaceChoice FindInterface(string? id, string? name) => Interfaces.FirstOrDefault(item =>
        string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(name) && item.Name == name))
        ?? new(id ?? string.Empty, name ?? string.Empty, string.IsNullOrWhiteSpace(name) ? Strings.AutomaticInterface : name);

    private static AccountConfig ToAccountConfig(DisplayedAccountConfig? account) => account is null
        ? new()
        : new() { AccountId = account.AccountId, AccountType = account.AccountType };

    private static List<string> SplitLines(string value) => value
        .Split(["\r\n", "\r", "\n"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    private static string AppendLine(string existing, string value) => string.IsNullOrWhiteSpace(existing)
        ? value
        : existing.Split(["\r\n", "\r", "\n"], StringSplitOptions.TrimEntries).Contains(value, StringComparer.Ordinal)
            ? existing
            : existing.TrimEnd() + Environment.NewLine + value;
}
