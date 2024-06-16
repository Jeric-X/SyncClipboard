using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.ViewModels;

public partial class SyncSettingViewModel : ObservableObject
{
    #region server
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNormalClientEnable))]
    private bool serverEnable;
    partial void OnServerEnableChanged(bool value) => ServerConfig = ServerConfig with { SwitchOn = value };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNormalClientEnable))]
    private bool clientMixedMode;
    partial void OnClientMixedModeChanged(bool value) => ServerConfig = ServerConfig with { ClientMixedMode = value };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerConfigDescription))]
    private ServerConfig serverConfig;
    partial void OnServerConfigChanged(ServerConfig value)
    {
        ServerEnable = value.SwitchOn;
        ClientMixedMode = value.ClientMixedMode;
        _configManager.SetConfig(value);
    }

    #endregion

    #region client
    [ObservableProperty]
    private bool syncEnable;
    partial void OnSyncEnableChanged(bool value) => ClientConfig = ClientConfig with { SyncSwitchOn = value };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseManulServer))]
    private bool useLocalServer;
    partial void OnUseLocalServerChanged(bool value) => ClientConfig = ClientConfig with { UseLocalServer = value };

    [ObservableProperty]
    private uint intervalTime;
    partial void OnIntervalTimeChanged(uint value) => ClientConfig = ClientConfig with { IntervalTime = value };

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
    private bool trustInsecureCertificate;
    partial void OnTrustInsecureCertificateChanged(bool value) => ClientConfig = ClientConfig with { TrustInsecureCertificate = value };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClientConfigDescription))]
    private SyncConfig clientConfig;
    partial void OnClientConfigChanged(SyncConfig value)
    {
        IntervalTime = value.IntervalTime;
        SyncEnable = value.SyncSwitchOn;
        UseLocalServer = value.UseLocalServer;
        TimeOut = value.TimeOut;
        MaxFileSize = value.MaxFileByte / 1024 / 1024;
        AutoDeleleServerFile = value.DeletePreviousFilesOnPush;
        NotifyOnDownloaded = value.NotifyOnDownloaded;
        NotifyOnManualUpload = value.NotifyOnManualUpload;
        TrustInsecureCertificate = value.TrustInsecureCertificate;
        _configManager.SetConfig(value);
    }

    [RelayCommand]
    private void LoginWithNextcloud()
    {
        _mainVM.NavigateToNextLevel(PageDefinition.NextCloudLogIn);
    }

    #endregion

    #region for view only

    public bool IsNormalClientEnable => !ServerEnable || !ClientMixedMode;

    public bool UseManulServer => !UseLocalServer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerConfigDescription))]
    public bool showServerPassword = false;

    public string ServerConfigDescription =>
@$"{I18n.Strings.Port}{new string('\t', int.Parse(I18n.Strings.PortTabRepeat))}: {ServerConfig.Port}
{I18n.Strings.UserName}{new string('\t', int.Parse(I18n.Strings.UserNameTabRepeat))}: {ServerConfig.UserName}
{I18n.Strings.Password}{new string('\t', int.Parse(I18n.Strings.PasswordTabRepeat))}: {GetPasswordString(ServerConfig.Password, ShowServerPassword)}";

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
        _configManager.ListenConfig<ServerConfig>(config => ServerConfig = config);
        serverConfig = _configManager.GetConfig<ServerConfig>();
        serverEnable = serverConfig.SwitchOn;
        clientMixedMode = serverConfig.ClientMixedMode;

        _configManager.ListenConfig<SyncConfig>(config => ClientConfig = config);
        clientConfig = _configManager.GetConfig<SyncConfig>();
        intervalTime = clientConfig.IntervalTime;
        syncEnable = clientConfig.SyncSwitchOn;
        useLocalServer = clientConfig.UseLocalServer;
        timeOut = clientConfig.TimeOut;
        maxFileSize = clientConfig.MaxFileByte / 1024 / 1024;
        autoDeleleServerFile = clientConfig.DeletePreviousFilesOnPush;
        notifyOnDownloaded = clientConfig.NotifyOnDownloaded;
        notifyOnManualUpload = clientConfig.NotifyOnManualUpload;
        trustInsecureCertificate = clientConfig.TrustInsecureCertificate;
    }

    public string? SetServerConfig(string portString, string username, string password)
    {
        if (!ushort.TryParse(portString, out var port))
        {
            return I18n.Strings.PortRangeIs;
        }
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return I18n.Strings.UsernameOrPasswordBlank;
        }

        ServerConfig = ServerConfig with { Password = password, Port = port, UserName = username };

        return null;
    }

    public string? SetClientConfig(string url, string username, string password)
    {
        ClientConfig = ClientConfig with { RemoteURL = url, UserName = username, Password = password };
        return null;
    }
}
