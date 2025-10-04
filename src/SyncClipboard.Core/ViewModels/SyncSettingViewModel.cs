using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public partial class SyncSettingViewModel : ObservableObject
{
    #region account management
    [ObservableProperty]
    private string currentAccountName = "未登录";

    [ObservableProperty]
    private bool isLoggedIn = false;

    [ObservableProperty]
    private bool hasMultipleAccounts = false;

    [RelayCommand]
    private void AddAccount()
    {
        _mainVM.NavigateToNextLevel(PageDefinition.AddAccount);
    }

    [RelayCommand]
    private void RemoveAccount()
    {
        // TODO: 实现移除账号逻辑
    }

    [RelayCommand]
    private void SelectAccount()
    {
        // TODO: 实现选择账号逻辑
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
    private bool autoDeleleServerFile;
    partial void OnAutoDeleleServerFileChanged(bool value) => ClientConfig = ClientConfig with { DeletePreviousFilesOnPush = value };

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
    [NotifyPropertyChangedFor(nameof(ClientConfigDescription))]
    private SyncConfig clientConfig;
    partial void OnClientConfigChanged(SyncConfig value)
    {
        IntervalTime = value.IntervalTime;
        RetryTimes = value.RetryTimes;
        SyncEnable = value.SyncSwitchOn;
        TimeOut = value.TimeOut;
        MaxFileSize = value.MaxFileByte / 1024 / 1024;
        AutoDeleleServerFile = value.DeletePreviousFilesOnPush;
        NotifyOnDownloaded = value.NotifyOnDownloaded;
        NotifyOnManualUpload = value.NotifyOnManualUpload;
        DoNotUploadWhenCut = value.DoNotUploadWhenCut;
        TrustInsecureCertificate = value.TrustInsecureCertificate;
        UploadEnable = value.PushSwitchOn;
        DownloadEnable = value.PullSwitchOn;
        TextEnable = value.EnableUploadText;
        SingleFileEnable = value.EnableUploadSingleFile;
        MultiFileEnable = value.EnableUploadMultiFile;
        _configManager.SetConfig(value);
    }

    [RelayCommand]
    private void LoginWithNextcloud()
    {
        _mainVM.NavigateToNextLevel(PageDefinition.NextCloudLogIn);
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

    #region for view only

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClientConfigDescription))]
    public bool showClientPassword = false;

    public string ClientConfigDescription =>
@$"{I18n.Strings.Address}{new string('\t', int.Parse(I18n.Strings.PortTabRepeat))}: {ClientConfig.RemoteURL}
{I18n.Strings.UserName}{new string('\t', int.Parse(I18n.Strings.UserNameTabRepeat))}: {ClientConfig.UserName}
{I18n.Strings.Password}{new string('\t', int.Parse(I18n.Strings.PasswordTabRepeat))}: {GetPasswordString(ClientConfig.Password, ShowClientPassword)}";

    private static string GetPasswordString(string origin, bool? show)
    {
        return show ?? false ? origin : "*********";
    }

    #endregion

    private readonly ConfigManager _configManager;
    private readonly MainViewModel _mainVM;

    public SyncSettingViewModel(ConfigManager configManager, MainViewModel mainViewModel)
    {
        _configManager = configManager;
        _mainVM = mainViewModel;

        _configManager.ListenConfig<SyncConfig>(config => ClientConfig = config);
        clientConfig = _configManager.GetConfig<SyncConfig>();
        intervalTime = clientConfig.IntervalTime;
        retryTimes = clientConfig.RetryTimes;
        syncEnable = clientConfig.SyncSwitchOn;
        timeOut = clientConfig.TimeOut;
        maxFileSize = clientConfig.MaxFileByte / 1024 / 1024;
        autoDeleleServerFile = clientConfig.DeletePreviousFilesOnPush;
        notifyOnDownloaded = clientConfig.NotifyOnDownloaded;
        notifyOnManualUpload = clientConfig.NotifyOnManualUpload;
        doNotUploadWhenCut = clientConfig.DoNotUploadWhenCut;
        trustInsecureCertificate = clientConfig.TrustInsecureCertificate;
        uploadEnable = clientConfig.PushSwitchOn;
        downloadEnable = clientConfig.PullSwitchOn;
        textEnable = clientConfig.EnableUploadText;
        singleFileEnable = clientConfig.EnableUploadSingleFile;
        multiFileEnable = clientConfig.EnableUploadMultiFile;
    }

    public string? SetClientConfig(string url, string username, string password)
    {
        ClientConfig = ClientConfig with { RemoteURL = url, UserName = username, Password = password };
        return null;
    }
}
