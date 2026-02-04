using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using System.Collections.ObjectModel;

namespace SyncClipboard.Core.ViewModels;

public partial class SyncSettingViewModel : ObservableObject
{
    #region account management
    [ObservableProperty]
    private DisplayedAccountConfig? selectedAccount;

    [ObservableProperty]
    private bool isLoggedIn = false;

    [ObservableProperty]
    private bool hasMultipleAccounts = false;

    [ObservableProperty]
    private bool showQueryInterval = false;

    public bool IsNotLoggedIn => !IsLoggedIn;

    public ObservableCollection<DisplayedAccountConfig> SavedAccounts { get; } = [];

    partial void OnIsLoggedInChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotLoggedIn));
    }

    partial void OnSelectedAccountChanged(DisplayedAccountConfig? value)
    {
        if (value != null)
        {
            var accountConfig = new AccountConfig
            {
                AccountId = value.AccountId,
                AccountType = value.AccountType
            };

            _configManager.SetConfig(accountConfig);
            UpdateShowQueryInterval(value.AccountType);
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
        if (SelectedAccount != null)
        {
            // 显示确认对话框
            var confirmed = await _dialog.ShowConfirmationAsync(
                Strings.ConfirmDelete,
                string.Format(Strings.DeleteAccountConfirmMessage, SelectedAccount.DisplayName));

            if (!confirmed)
            {
                return;
            }
            _ = _accountManager.RemoveConfig(SelectedAccount.AccountType, SelectedAccount.AccountId);
        }
    }

    [RelayCommand]
    private void EditAccount()
    {
        if (SelectedAccount != null)
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
        SavedAccounts.Clear();
        var accountsToLoad = accounts ?? _accountManager.GetSavedAccounts();
        foreach (var account in accountsToLoad)
        {
            SavedAccounts.Add(account);
        }

        HasMultipleAccounts = SavedAccounts.Count > 1;
        IsLoggedIn = SavedAccounts.Count > 0;

        var currentConfig = _configManager.GetConfig<AccountConfig>();
        SelectedAccount = SavedAccounts.FirstOrDefault(a =>
            a.AccountId == currentConfig.AccountId &&
            a.AccountType == currentConfig.AccountType);

        if (SelectedAccount != null)
        {
            UpdateShowQueryInterval(SelectedAccount.AccountType);
        }
    }

    private void UpdateShowQueryInterval(string accountType)
    {
        ShowQueryInterval = accountType != OfficialConfig.ConfigTypeName;
    }
    #endregion

    #region client
    [ObservableProperty]
    private bool syncEnable;
    partial void OnSyncEnableChanged(bool value) => ClientConfig = ClientConfig with { SyncSwitchOn = value };

    [ObservableProperty]
    private uint intervalTime;
    partial void OnIntervalTimeChanged(uint value) => ClientConfig = ClientConfig with { IntervalTime = value };

    [ObservableProperty]
    private uint retryTimes;
    partial void OnRetryTimesChanged(uint value) => ClientConfig = ClientConfig with { RetryTimes = value };

    [ObservableProperty]
    private uint timeOut;
    partial void OnTimeOutChanged(uint value) => ClientConfig = ClientConfig with { TimeOut = value };

    [ObservableProperty]
    private uint maxFileSize;
    partial void OnMaxFileSizeChanged(uint value) => ClientConfig = ClientConfig with { MaxFileByte = value * 1024 * 1024 };

    [ObservableProperty]
    private bool notifyOnDownloaded;
    partial void OnNotifyOnDownloadedChanged(bool value) => ClientConfig = ClientConfig with { NotifyOnDownloaded = value };

    [ObservableProperty]
    private bool notifyOnManualUpload;
    partial void OnNotifyOnManualUploadChanged(bool value) => ClientConfig = ClientConfig with { NotifyOnManualUpload = value };

    [ObservableProperty]
    private bool doNotUploadWhenCut;
    partial void OnDoNotUploadWhenCutChanged(bool value) => ClientConfig = ClientConfig with { DoNotUploadWhenCut = value };

    [ObservableProperty]
    private bool ignoreExcludeForSyncSuggestion;
    partial void OnIgnoreExcludeForSyncSuggestionChanged(bool value) => ClientConfig = ClientConfig with { IgnoreExcludeForSyncSuggestion = value };

    [ObservableProperty]
    private bool notifyFileSyncProgress;
    partial void OnNotifyFileSyncProgressChanged(bool value) => ClientConfig = ClientConfig with { NotifyFileSyncProgress = value };

    [ObservableProperty]
    private bool trustInsecureCertificate;
    partial void OnTrustInsecureCertificateChanged(bool value) => ClientConfig = ClientConfig with { TrustInsecureCertificate = value };

    [ObservableProperty]
    private bool uploadEnable;
    partial void OnUploadEnableChanged(bool value) => ClientConfig = ClientConfig with { PushSwitchOn = value };

    [ObservableProperty]
    private bool downloadEnable;
    partial void OnDownloadEnableChanged(bool value) => ClientConfig = ClientConfig with { PullSwitchOn = value };

    [ObservableProperty]
    private bool textEnable;
    partial void OnTextEnableChanged(bool value) => ClientConfig = ClientConfig with { EnableUploadText = value };

    [ObservableProperty]
    private bool singleFileEnable;
    partial void OnSingleFileEnableChanged(bool value) => ClientConfig = ClientConfig with { EnableUploadSingleFile = value };

    [ObservableProperty]
    private bool multiFileEnable;
    partial void OnMultiFileEnableChanged(bool value) => ClientConfig = ClientConfig with { EnableUploadMultiFile = value };

    [ObservableProperty]
    private SyncConfig clientConfig;
    partial void OnClientConfigChanged(SyncConfig value)
    {
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
        TrustInsecureCertificate = value.TrustInsecureCertificate;
        UploadEnable = value.PushSwitchOn;
        DownloadEnable = value.PullSwitchOn;
        TextEnable = value.EnableUploadText;
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
    private void OpenSyncContentControlPage()
    {
        _mainVM.NavigateToNextLevel(PageDefinition.SyncContentControl);
    }

    #endregion

    private readonly ConfigManager _configManager;
    private readonly MainViewModel _mainVM;
    private readonly AccountManager _accountManager;
    private readonly IMainWindowDialog _dialog;

    public SyncSettingViewModel(ConfigManager configManager, MainViewModel mainViewModel, AccountManager accountManager, IMainWindowDialog dialog)
    {
        _configManager = configManager;
        _mainVM = mainViewModel;
        _accountManager = accountManager;
        _dialog = dialog;

        _configManager.ListenConfig<SyncConfig>(config => ClientConfig = config);
        _accountManager.SavedAccountsChanged += OnSavedAccountsChanged;

        clientConfig = _configManager.GetConfig<SyncConfig>();
        intervalTime = clientConfig.IntervalTime;
        retryTimes = clientConfig.RetryTimes;
        syncEnable = clientConfig.SyncSwitchOn;
        timeOut = clientConfig.TimeOut;
        maxFileSize = clientConfig.MaxFileByte / 1024 / 1024;
        notifyOnDownloaded = clientConfig.NotifyOnDownloaded;
        notifyOnManualUpload = clientConfig.NotifyOnManualUpload;
        doNotUploadWhenCut = clientConfig.DoNotUploadWhenCut;
        ignoreExcludeForSyncSuggestion = clientConfig.IgnoreExcludeForSyncSuggestion;
        notifyFileSyncProgress = clientConfig.NotifyFileSyncProgress;
        trustInsecureCertificate = clientConfig.TrustInsecureCertificate;
        uploadEnable = clientConfig.PushSwitchOn;
        downloadEnable = clientConfig.PullSwitchOn;
        textEnable = clientConfig.EnableUploadText;
        singleFileEnable = clientConfig.EnableUploadSingleFile;
        multiFileEnable = clientConfig.EnableUploadMultiFile;

        LoadSavedAccounts();
    }
}
