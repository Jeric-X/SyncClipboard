using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.Utilities.Network;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public partial class SyncSettingViewModel : ObservableObject
{
    // Preserve saved values until every setting has been loaded.
    private readonly bool _isInitializing = true;

    #region account management
    private static readonly DisplayedAccountConfig NoAccountOption = new()
    {
        AccountId = string.Empty,
        AccountType = string.Empty,
        DisplayName = Strings.NoAccountSelected,
    };

    [ObservableProperty]
    public partial DisplayedAccountConfig? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial bool IsLoggedIn { get; set; } = false;

    [ObservableProperty]
    public partial bool HasMultipleAccounts { get; set; } = false;

    [ObservableProperty]
    public partial bool ShowQueryInterval { get; set; } = false;

    [ObservableProperty]
    public partial string AccountAutoSwitchDescription { get; set; } = Strings.Disabled;

    [ObservableProperty]
    public partial bool AccountAutoSwitchEnabled { get; set; }

    partial void OnAccountAutoSwitchEnabledChanged(bool value)
    {
        if (_isInitializing) return;

        var config = _configManager.GetConfig<NetworkAccountSwitchConfig>();
        if (config.Enabled != value)
        {
            _configManager.SetConfig(config with { Enabled = value });
        }
    }

    private bool _isUpdatingSelectedAccount;

    public bool IsNotLoggedIn => !IsLoggedIn;

    public bool HasSelectedAccount => SelectedAccount is not null
        && !string.IsNullOrWhiteSpace(SelectedAccount.AccountId)
        && !string.IsNullOrWhiteSpace(SelectedAccount.AccountType);

    public ObservableCollection<DisplayedAccountConfig> SavedAccounts { get; } = [];

    partial void OnIsLoggedInChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotLoggedIn));
    }

    partial void OnSelectedAccountChanged(DisplayedAccountConfig? value)
    {
        OnPropertyChanged(nameof(HasSelectedAccount));
        if (value != null && !_isUpdatingSelectedAccount)
        {
            var accountConfig = new AccountConfig
            {
                AccountId = value.AccountId,
                AccountType = value.AccountType
            };

            _accountManager.SelectAccount(accountConfig, AccountManager.AccountSelectionOrigin.Manual);
            ShowQueryInterval = !accountConfig.IsEmpty() && value.AccountType != OfficialConfig.ConfigTypeName;
        }
    }

    [RelayCommand]
    private void AddAccount()
    {
        _mainVM.NavigateToNextLevel(PageDefinition.AddAccount);
    }

    [RelayCommand]
    private async Task RemoveAccount()
    {
        var selectedAccount = SelectedAccount;
        if (HasSelectedAccount && selectedAccount != null)
        {
            var account = new AccountConfig
            {
                AccountId = selectedAccount.AccountId,
                AccountType = selectedAccount.AccountType,
            };
            var switchConfig = _configManager.GetConfig<NetworkAccountSwitchConfig>();
            var affectedRuleCount = switchConfig.Rules.Count(rule =>
                rule.TargetAccount.AccountId == account.AccountId
                && rule.TargetAccount.AccountType == account.AccountType);
            var message = string.Format(Strings.DeleteAccountConfirmMessage, selectedAccount.DisplayName);
            if (affectedRuleCount > 0)
            {
                message += Environment.NewLine + Environment.NewLine
                    + string.Format(Strings.AccountRuleCleanup, affectedRuleCount);
            }

            var confirmed = await _dialog.ShowConfirmationAsync(Strings.ConfirmDelete, message);

            if (!confirmed)
            {
                return;
            }
            var cleanup = NetworkAccountSwitchConfigCleaner.RemoveAccount(switchConfig, account);
            if (cleanup.RemovedRuleCount > 0 || cleanup.RemovedDefaultAccount)
            {
                _configManager.SetConfig(cleanup.Config);
            }
            _ = _accountManager.RemoveConfig(account.AccountType, account.AccountId);
        }
    }

    [RelayCommand]
    private void EditAccount()
    {
        if (HasSelectedAccount && SelectedAccount != null)
        {
            var accountConfig = new AccountConfig
            {
                AccountId = SelectedAccount.AccountId,
                AccountType = SelectedAccount.AccountType
            };
            _mainVM.NavigateToNextLevel(PageDefinition.DefaultAddAccount, accountConfig);
        }
    }

    private void OnSavedAccountsChanged(IEnumerable<DisplayedAccountConfig> newAccounts)
    {
        LoadSavedAccounts(newAccounts);
    }

    private void LoadSavedAccounts(IEnumerable<DisplayedAccountConfig>? accounts = null)
    {
        var actualAccounts = (accounts ?? _accountManager.GetSavedAccounts()).ToList();
        SavedAccounts.Clear();
        SavedAccounts.Add(NoAccountOption);
        foreach (var account in actualAccounts)
        {
            SavedAccounts.Add(account);
        }

        HasMultipleAccounts = actualAccounts.Count > 1;
        IsLoggedIn = actualAccounts.Count > 0;

        var currentConfig = _configManager.GetConfig<AccountConfig>();
        SetSelectedAccount(currentConfig);
    }

    [RelayCommand]
    private void OpenNetworkAccountSwitch() => _mainVM.NavigateToNextLevel(PageDefinition.NetworkAccountSwitch);

    private void OnNetworkAccountSwitchStatusChanged(object? sender, EventArgs e) =>
        _ = _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            if (_networkAccountSwitchStatusActive)
            {
                AccountAutoSwitchDescription = NetworkAccountSwitchStatusFormatter.Format(_networkAccountSwitchService.Status);
            }
        });

    private void OnNetworkAccountSwitchConfigChanged(NetworkAccountSwitchConfig config) =>
        _ = _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            AccountAutoSwitchEnabled = config.Enabled;
        });

    private void SetSelectedAccount(AccountConfig accountConfig)
    {
        _isUpdatingSelectedAccount = true;
        try
        {
            SelectedAccount = accountConfig.IsEmpty()
                ? NoAccountOption
                : SavedAccounts.FirstOrDefault(a =>
                    a.AccountId == accountConfig.AccountId
                    && a.AccountType == accountConfig.AccountType)
                    ?? NoAccountOption;
            ShowQueryInterval = HasSelectedAccount
                && SelectedAccount.AccountType != OfficialConfig.ConfigTypeName;
        }
        finally
        {
            _isUpdatingSelectedAccount = false;
        }
    }

    private void OnCurrentAccountChanged(AccountConfig accountConfig, object? _)
    {
        _ = _threadDispatcher.RunOnMainThreadAsync(() => SetSelectedAccount(accountConfig));
    }

    #endregion

    #region client
    [ObservableProperty]
    public partial bool SyncEnable { get; set; }

    partial void OnSyncEnableChanged(bool value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { SyncSwitchOn = value };
    }

    [ObservableProperty]
    public partial uint IntervalTime { get; set; }

    partial void OnIntervalTimeChanged(uint value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { IntervalTime = value };
    }

    [ObservableProperty]
    public partial uint RetryTimes { get; set; }

    partial void OnRetryTimesChanged(uint value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { RetryTimes = value };
    }

    [ObservableProperty]
    public partial uint TimeOut { get; set; }

    partial void OnTimeOutChanged(uint value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { TimeOut = value };
    }

    [ObservableProperty]
    public partial uint MaxFileSize { get; set; }

    partial void OnMaxFileSizeChanged(uint value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { MaxFileByte = value * 1024 * 1024 };
    }

    [ObservableProperty]
    public partial bool NotifyOnDownloaded { get; set; }

    partial void OnNotifyOnDownloadedChanged(bool value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { NotifyOnDownloaded = value };
    }

    [ObservableProperty]
    public partial bool NotifyOnManualUpload { get; set; }

    partial void OnNotifyOnManualUploadChanged(bool value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { NotifyOnManualUpload = value };
    }

    [ObservableProperty]
    public partial bool DoNotUploadWhenCut { get; set; }

    partial void OnDoNotUploadWhenCutChanged(bool value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { DoNotUploadWhenCut = value };
    }

    [ObservableProperty]
    public partial bool IgnoreExcludeForSyncSuggestion { get; set; }

    partial void OnIgnoreExcludeForSyncSuggestionChanged(bool value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { IgnoreExcludeForSyncSuggestion = value };
    }

    [ObservableProperty]
    public partial bool NotifyFileSyncProgress { get; set; }

    partial void OnNotifyFileSyncProgressChanged(bool value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { NotifyFileSyncProgress = value };
    }

    [ObservableProperty]
    public partial bool UploadEnable { get; set; }

    partial void OnUploadEnableChanged(bool value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { PushSwitchOn = value };
    }

    [ObservableProperty]
    public partial bool DownloadEnable { get; set; }

    partial void OnDownloadEnableChanged(bool value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { PullSwitchOn = value };
    }

    [ObservableProperty]
    public partial bool TextEnable { get; set; }

    partial void OnTextEnableChanged(bool value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { EnableUploadText = value };
    }

    [ObservableProperty]
    public partial bool ImageEnable { get; set; }

    partial void OnImageEnableChanged(bool value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { EnableUploadImage = value };
    }

    [ObservableProperty]
    public partial bool SingleFileEnable { get; set; }

    partial void OnSingleFileEnableChanged(bool value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { EnableUploadSingleFile = value };
    }

    [ObservableProperty]
    public partial bool MultiFileEnable { get; set; }

    partial void OnMultiFileEnableChanged(bool value)
    {
        if (_isInitializing) return;
        ClientConfig = ClientConfig with { EnableUploadMultiFile = value };
    }

    [ObservableProperty]
    public partial SyncConfig ClientConfig { get; set; }

    partial void OnClientConfigChanged(SyncConfig value)
    {
        if (_isInitializing) return;

        IntervalTime = value.IntervalTime;
        RetryTimes = value.RetryTimes;
        SyncEnable = value.SyncSwitchOn;
        TimeOut = value.TimeOut;
        MaxFileSize = value.MaxFileByte / 1024 / 1024;
        NotifyOnDownloaded = value.NotifyOnDownloaded;
        NotifyOnManualUpload = value.NotifyOnManualUpload;
        DoNotUploadWhenCut = value.DoNotUploadWhenCut;
        IgnoreExcludeForSyncSuggestion = value.IgnoreExcludeForSyncSuggestion;
        NotifyFileSyncProgress = value.NotifyFileSyncProgress;
        UploadEnable = value.PushSwitchOn;
        DownloadEnable = value.PullSwitchOn;
        TextEnable = value.EnableUploadText;
        ImageEnable = value.EnableUploadImage;
        SingleFileEnable = value.EnableUploadSingleFile;
        MultiFileEnable = value.EnableUploadMultiFile;
        _configManager.SetConfig(value);
    }

    [RelayCommand]
    private void SetFileSyncFilter()
    {
        _mainVM.NavigateToNextLevel(PageDefinition.FileSyncFilterSetting);
    }

    [RelayCommand]
    private void SetClipboardOwnerFilter()
    {
        _mainVM.NavigateToNextLevel(PageDefinition.ClipboardOwnerFilterSetting, ClipboardOwnerFilterConfig.ConfigKey);
    }

    [RelayCommand]
    private void OpenSyncContentControlPage()
    {
        _mainVM.NavigateToNextLevel(PageDefinition.SyncContentControl);
    }

    [RelayCommand]
    private void OpenClipboardAcquisitionRulesPage()
    {
        _mainVM.NavigateToNextLevel(PageDefinition.ClipboardAcquisitionRules);
    }

    #endregion

    #region clipboard source (Linux only)

    public bool IsLinux { get; } = OperatingSystem.IsLinux();

    [ObservableProperty]
    public partial bool WlClipboardEnabled { get; set; }

    partial void OnWlClipboardEnabledChanged(bool value) => UpdateProhibitSource("wl-clipboard", value);

    [ObservableProperty]
    public partial bool XClipEnabled { get; set; }

    partial void OnXClipEnabledChanged(bool value) => UpdateProhibitSource("xclip", value);

    [ObservableProperty]
    public partial bool AvaloniaEnabled { get; set; }

    partial void OnAvaloniaEnabledChanged(bool value) => UpdateProhibitSource("Avalonia", value);

    private void UpdateProhibitSource(string sourceName, bool enabled)
    {
        var config = _configManager.GetConfig<ClipboardFactoryConfig>();
        var list = new List<string>(config.ProhibitSources);
        if (enabled)
            list.Remove(sourceName);
        else if (!list.Contains(sourceName))
            list.Add(sourceName);
        _configManager.SetConfig(new ClipboardFactoryConfig { ProhibitSources = list });
    }

    private void LoadClipboardFactoryConfig(ClipboardFactoryConfig config)
    {
        WlClipboardEnabled = !config.ProhibitSources.Contains("wl-clipboard");
        XClipEnabled = !config.ProhibitSources.Contains("xclip");
        AvaloniaEnabled = !config.ProhibitSources.Contains("Avalonia");
    }

    #endregion

    private readonly ConfigManager _configManager;
    private readonly MainViewModel _mainVM;
    private readonly AccountManager _accountManager;
    private readonly IMainWindowDialog _dialog;
    private readonly IThreadDispatcher _threadDispatcher;
    private readonly NetworkAccountSwitchService _networkAccountSwitchService;
    private bool _networkAccountSwitchStatusActive;

    public void ActivateNetworkAccountSwitchStatus()
    {
        if (_networkAccountSwitchStatusActive) return;
        _networkAccountSwitchStatusActive = true;
        _networkAccountSwitchService.StatusChanged += OnNetworkAccountSwitchStatusChanged;
        AccountAutoSwitchDescription = NetworkAccountSwitchStatusFormatter.Format(_networkAccountSwitchService.Status);
    }

    public void DeactivateNetworkAccountSwitchStatus()
    {
        if (!_networkAccountSwitchStatusActive) return;
        _networkAccountSwitchStatusActive = false;
        _networkAccountSwitchService.StatusChanged -= OnNetworkAccountSwitchStatusChanged;
    }

    public SyncSettingViewModel(
        ConfigManager configManager,
        MainViewModel mainViewModel,
        AccountManager accountManager,
        IMainWindowDialog dialog,
        IThreadDispatcher threadDispatcher,
        NetworkAccountSwitchService networkAccountSwitchService)
    {
        _configManager = configManager;
        _mainVM = mainViewModel;
        _accountManager = accountManager;
        _dialog = dialog;
        _threadDispatcher = threadDispatcher;
        _networkAccountSwitchService = networkAccountSwitchService;

        _configManager.ListenConfig<SyncConfig>(config => ClientConfig = config);
        _configManager.ListenConfig<ClipboardFactoryConfig>(LoadClipboardFactoryConfig);
        _configManager.ListenConfig<NetworkAccountSwitchConfig>(OnNetworkAccountSwitchConfigChanged);
        _accountManager.SavedAccountsChanged += OnSavedAccountsChanged;
        _accountManager.CurrentAccountChanged += OnCurrentAccountChanged;
        AccountAutoSwitchEnabled = _configManager.GetConfig<NetworkAccountSwitchConfig>().Enabled;
        AccountAutoSwitchDescription = NetworkAccountSwitchStatusFormatter.Format(_networkAccountSwitchService.Status);
        ClientConfig = _configManager.GetConfig<SyncConfig>();
        IntervalTime = ClientConfig.IntervalTime;
        RetryTimes = ClientConfig.RetryTimes;
        SyncEnable = ClientConfig.SyncSwitchOn;
        TimeOut = ClientConfig.TimeOut;
        MaxFileSize = ClientConfig.MaxFileByte / 1024 / 1024;
        NotifyOnDownloaded = ClientConfig.NotifyOnDownloaded;
        NotifyOnManualUpload = ClientConfig.NotifyOnManualUpload;
        DoNotUploadWhenCut = ClientConfig.DoNotUploadWhenCut;
        IgnoreExcludeForSyncSuggestion = ClientConfig.IgnoreExcludeForSyncSuggestion;
        NotifyFileSyncProgress = ClientConfig.NotifyFileSyncProgress;
        UploadEnable = ClientConfig.PushSwitchOn;
        DownloadEnable = ClientConfig.PullSwitchOn;
        TextEnable = ClientConfig.EnableUploadText;
        ImageEnable = ClientConfig.EnableUploadImage;
        SingleFileEnable = ClientConfig.EnableUploadSingleFile;
        MultiFileEnable = ClientConfig.EnableUploadMultiFile;

        _isInitializing = false;
        LoadClipboardFactoryConfig(_configManager.GetConfig<ClipboardFactoryConfig>());

        LoadSavedAccounts();
    }
}
