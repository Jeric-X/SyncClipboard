using NativeNotification.Interface;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Network;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.UserServices;

public sealed class NetworkAccountSwitchService(
    ConfigManager configManager,
    AccountManager accountManager,
    INetworkContextProvider networkContextProvider,
    INotificationManager notificationManager,
    Interfaces.ILogger logger) : Service
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly ConfigManager _configManager = configManager;
    private readonly AccountManager _accountManager = accountManager;
    private readonly INetworkContextProvider _networkContextProvider = networkContextProvider;
    private readonly INotificationManager _notificationManager = notificationManager;
    private readonly ILogger _logger = logger;
    private readonly SemaphoreSlim _evaluationLock = new(1, 1);
    private readonly AsyncLatestWinsDebouncer _debouncer = new();
    private readonly NetworkAccountSwitchRuntimeState _runtimeState = new();
    private readonly object _monitoringLock = new();

    private NetworkAccountSwitchConfig _config = configManager.GetConfig<NetworkAccountSwitchConfig>();
    private EventHandler? _statusChanged;
    private int _networkMonitoringDemandCount;
    private bool _serviceStarted;
    private bool _isMonitoring;
    private PeriodicTimer? _pollTimer;
    private CancellationTokenSource? _pollCancellation;

    public event EventHandler? StatusChanged
    {
        add { lock (_monitoringLock) _statusChanged += value; }
        remove { lock (_monitoringLock) _statusChanged -= value; }
    }
    public NetworkAccountSwitchStatus Status { get; private set; } = new();
    public NetworkContextSnapshot Snapshot { get; private set; } = new();
    public bool CanOpenWifiSettings => _networkContextProvider.CanOpenWifiSettings;

    protected override void StartService()
    {
        _serviceStarted = true;
        if (UpdateMonitoringState()) ScheduleEvaluation(force: true, immediate: true);
    }

    protected override void StopSerivce()
    {
        _serviceStarted = false;
        UpdateMonitoringState();
    }

    public override void Load()
    {
        var config = _configManager.GetConfig<NetworkAccountSwitchConfig>();
        if (config.Equals(_config))
        {
            return;
        }

        _config = config;
        _runtimeState.OnConfigurationChanged();
        UpdateMonitoringState();
        ScheduleEvaluation(force: true, immediate: true);
    }

    public Task RefreshAsync(bool requestWifiAccess = false, CancellationToken cancellationToken = default) =>
        EvaluateAsync(force: true, requestWifiAccess, cancellationToken);

    public void OpenWifiSettings() => _networkContextProvider.OpenWifiSettings();

    public void AddNetworkMonitoringDemand()
    {
        lock (_monitoringLock) _networkMonitoringDemandCount++;
        UpdateMonitoringState();
    }

    public void RemoveNetworkMonitoringDemand()
    {
        lock (_monitoringLock)
        {
            _networkMonitoringDemandCount = Math.Max(0, _networkMonitoringDemandCount - 1);
        }
        UpdateMonitoringState();
    }

    private async Task PollAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                ScheduleEvaluation(force: false, immediate: true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        var hadManualOverride = _runtimeState.OnNetworkChanged();
        ScheduleEvaluation(force: hadManualOverride, immediate: false);
    }

    private void OnAccountSelectionChanged(object? sender, AccountManager.AccountSelectionChangedEventArgs e)
    {
        if (!_config.Enabled) return;
        if (e.Origin is not (AccountManager.AccountSelectionOrigin.Manual or AccountManager.AccountSelectionOrigin.External))
        {
            return;
        }

        _runtimeState.OnManualSelection();
        UpdateStatus(new()
        {
            State = NetworkAccountSwitchState.ManualOverride,
            AccountName = GetAccountName(e.Account),
        });
    }

    private void ScheduleEvaluation(bool force, bool immediate)
    {
        _ = _debouncer.ScheduleAsync(
            token => EvaluateAsync(force, requestWifiAccess: false, token),
            immediate ? TimeSpan.Zero : DebounceDelay);
    }

    private bool UpdateMonitoringState()
    {
        lock (_monitoringLock)
        {
            var demand = NetworkMonitoringDemand.Calculate(
                _config.Enabled,
                _config.Rules.Any(rule => rule.Enabled && rule.WifiSsids.Count > 0),
                _networkMonitoringDemandCount > 0);

            var started = UpdateNetworkChangeSubscription(demand);
            UpdateWifiPolling(demand);
            return started;
        }
    }

    // Caller must hold _monitoringLock.
    private bool UpdateNetworkChangeSubscription(NetworkMonitoringDemand demand)
    {
        var shouldMonitor = _serviceStarted && demand.ListenForNetworkChanges;
        if (shouldMonitor && !_isMonitoring)
        {
            _networkContextProvider.NetworkChanged += OnNetworkChanged;
            _accountManager.AccountSelectionChanged += OnAccountSelectionChanged;
            _isMonitoring = true;
            return true;
        }
        if (!shouldMonitor && _isMonitoring)
        {
            _networkContextProvider.NetworkChanged -= OnNetworkChanged;
            _accountManager.AccountSelectionChanged -= OnAccountSelectionChanged;
            _debouncer.CancelPending();
            _isMonitoring = false;
        }
        return false;
    }

    // Caller must hold _monitoringLock.
    private void UpdateWifiPolling(NetworkMonitoringDemand demand)
    {
        var shouldPollWifi = _serviceStarted && demand.PollWifi;
        if (shouldPollWifi && _pollTimer is null)
        {
            _pollCancellation = new();
            _pollTimer = new(PollInterval);
            _ = PollAsync(_pollTimer, _pollCancellation.Token);
            return;
        }
        if (!shouldPollWifi && _pollTimer is not null)
        {
            _pollCancellation?.Cancel();
            _pollCancellation?.Dispose();
            _pollCancellation = null;
            _pollTimer.Dispose();
            _pollTimer = null;
        }
    }

    private async Task EvaluateAsync(bool force, bool requestWifiAccess, CancellationToken cancellationToken)
    {
        await _evaluationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EvaluateCurrentNetworkAsync(force, requestWifiAccess, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Write("NetworkAccountSwitch", ex.Message);
            UpdateStatus(new() { State = NetworkAccountSwitchState.Error, Error = ex.Message });
        }
        finally
        {
            _evaluationLock.Release();
        }
    }

    private async Task EvaluateCurrentNetworkAsync(
        bool force,
        bool requestWifiAccess,
        CancellationToken cancellationToken)
    {
        Snapshot = await _networkContextProvider.GetCurrentAsync(requestWifiAccess, cancellationToken).ConfigureAwait(false);
        if (!_config.Enabled)
        {
            UpdateStatus(new() { State = NetworkAccountSwitchState.Disabled });
            return;
        }

        if (TryApplyManualOverrideStatus())
        {
            return;
        }

        if (!_runtimeState.ShouldEvaluate(Snapshot.Fingerprint, force))
        {
            return;
        }

        UpdateStatus(new() { State = NetworkAccountSwitchState.Evaluating });
        ApplyDecision(NetworkAccountSwitchEvaluator.Evaluate(_config, Snapshot, AccountExists));
    }

    private bool TryApplyManualOverrideStatus()
    {
        if (!_runtimeState.ManualOverride || _runtimeState.ShouldClearManualOverride(Snapshot.Fingerprint))
        {
            return false;
        }

        UpdateStatus(new()
        {
            State = NetworkAccountSwitchState.ManualOverride,
            AccountName = GetAccountName(_configManager.GetConfig<AccountConfig>()),
        });
        return true;
    }

    private void ApplyDecision(NetworkAccountSwitchDecision decision)
    {
        switch (decision.Kind)
        {
            case NetworkAccountSwitchDecisionKind.SwitchAccount when decision.Match is not null:
                ApplyMatch(decision.Match);
                break;
            case NetworkAccountSwitchDecisionKind.SwitchAccount when decision.TargetAccount is not null:
                ApplyDefaultAccount(decision.TargetAccount);
                break;
            case NetworkAccountSwitchDecisionKind.RemoveSyncAccount:
                ApplyRemoveSyncAccount(decision.Match);
                break;
            default:
                ApplyKeepCurrent();
                break;
        }
    }

    private void ApplyMatch(NetworkRuleMatchResult match)
    {
        var current = _configManager.GetConfig<AccountConfig>();
        var changed = current != match.Rule.TargetAccount;
        if (changed)
        {
            _accountManager.SelectAccount(match.Rule.TargetAccount, AccountManager.AccountSelectionOrigin.Automatic);
        }

        var accountName = GetAccountName(match.Rule.TargetAccount);
        UpdateStatus(new()
        {
            State = NetworkAccountSwitchState.Matched,
            RuleId = match.Rule.Id,
            RuleName = match.Rule.Name,
            AccountName = accountName,
        });

        if (changed && _config.NotifyOnChange)
        {
            _notificationManager.ShowText("SyncClipboard", $"{match.Rule.Name} → {accountName}");
        }
    }

    private void ApplyDefaultAccount(AccountConfig targetAccount)
    {
        var current = _configManager.GetConfig<AccountConfig>();
        var changed = current != targetAccount;
        if (changed)
        {
            _accountManager.SelectAccount(targetAccount, AccountManager.AccountSelectionOrigin.Automatic);
        }
        UpdateStatus(new()
        {
            State = NetworkAccountSwitchState.NoMatch,
            AccountName = GetAccountName(targetAccount),
        });
        if (changed && _config.NotifyOnChange)
        {
            _notificationManager.ShowText("SyncClipboard", $"{Strings.DefaultAccount} → {GetAccountName(targetAccount)}");
        }
    }

    private void ApplyRemoveSyncAccount(NetworkRuleMatchResult? match)
    {
        var current = _configManager.GetConfig<AccountConfig>();
        var changed = !current.IsEmpty();
        if (changed)
        {
            _accountManager.SelectAccount(new(), AccountManager.AccountSelectionOrigin.Automatic);
        }

        var detail = match is not null
            ? string.Format(Strings.RuleAccountMissing, match.Rule.Name)
            : null;
        UpdateStatus(new() { State = NetworkAccountSwitchState.AccountRemoved, RuleName = match?.Rule.Name, Detail = detail });
        if (changed && _config.NotifyOnChange)
        {
            _notificationManager.ShowText("SyncClipboard", detail ?? Strings.SyncAccountRemoved);
        }
    }

    private void ApplyKeepCurrent()
    {
        UpdateStatus(new() { State = NetworkAccountSwitchState.NoMatch });
    }

    private bool AccountExists(AccountConfig account) =>
        !string.IsNullOrWhiteSpace(account.AccountId)
        && !string.IsNullOrWhiteSpace(account.AccountType)
        && _accountManager.GetConfig(account.AccountType, account.AccountId) is not null;

    private string GetAccountName(AccountConfig account) =>
        _accountManager.GetSavedAccounts().FirstOrDefault(item =>
            item.AccountId == account.AccountId && item.AccountType == account.AccountType)?.DisplayName
        ?? $"{account.AccountId} - {account.AccountType}";

    private void UpdateStatus(NetworkAccountSwitchStatus status)
    {
        Status = status with { EvaluatedAt = DateTimeOffset.Now };
        EventHandler? handler;
        lock (_monitoringLock) handler = _statusChanged;
        handler?.Invoke(this, EventArgs.Empty);
    }
}
