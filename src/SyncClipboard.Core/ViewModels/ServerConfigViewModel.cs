using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public partial class ServerConfigViewModel : ObservableObject
{
    #region server properties
    [ObservableProperty]
    private bool serverEnable;
    partial void OnServerEnableChanged(bool value) => ServerConfig = ServerConfig with { SwitchOn = value };

    [ObservableProperty]
    private bool enableHttps;
    partial void OnEnableHttpsChanged(bool value) => ServerConfig = ServerConfig with { EnableHttps = value };

    public static readonly IEnumerable<string> CertificatePemFileTypes = [".pem"];
    [ObservableProperty]
    private string certificatePemPath = string.Empty;
    partial void OnCertificatePemPathChanged(string value) => ServerConfig = ServerConfig with { CertificatePemPath = value };

    public static readonly IEnumerable<string> CertificatePemKeyFileTypes = [".pem"];
    [ObservableProperty]
    private string certificatePemKeyPath = string.Empty;
    partial void OnCertificatePemKeyPathChanged(string value) => ServerConfig = ServerConfig with { CertificatePemKeyPath = value };

    [ObservableProperty]
    private bool enableCustomConfigurationFile;
    partial void OnEnableCustomConfigurationFileChanged(bool value) => ServerConfig = ServerConfig with { EnableCustomConfigurationFile = value };

    public static readonly IEnumerable<string> CustomConfigurationFileTypes = [".json"];
    [ObservableProperty]
    private string customConfigurationFilePath = string.Empty;
    partial void OnCustomConfigurationFilePathChanged(string value) => ServerConfig = ServerConfig with { CustomConfigurationFilePath = value };

    [ObservableProperty]
    private uint maxHistoryCount;
    partial void OnMaxHistoryCountChanged(uint value) => ServerConfig = ServerConfig with { MaxHistoryCount = value };

    [RelayCommand]
    private static void OpenCustomConfigDescLink()
    {
        Sys.OpenWithDefaultApp(I18n.Strings.CustomConfigFileLink);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerConfigDescription))]
    private ServerConfig serverConfig = new();
    partial void OnServerConfigChanged(ServerConfig value)
    {
        ServerEnable = value.SwitchOn;
        EnableHttps = value.EnableHttps;
        CertificatePemPath = value.CertificatePemPath;
        CertificatePemKeyPath = value.CertificatePemKeyPath;
        EnableCustomConfigurationFile = value.EnableCustomConfigurationFile;
        CustomConfigurationFilePath = value.CustomConfigurationFilePath;
        MaxHistoryCount = value.MaxHistoryCount;
        _configManager.SetConfig(value);

        OnPropertyChanged(nameof(ShowHttpsConfig));
        OnPropertyChanged(nameof(ShowHttpsCertConfig));
    }

    #endregion

    #region view properties
    public bool ShowHttpsConfig => !EnableCustomConfigurationFile;
    public bool ShowHttpsCertConfig => EnableHttps && !EnableCustomConfigurationFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerConfigDescription))]
    public bool showServerPassword = false;

    public string ServerConfigDescription =>
@$"{I18n.Strings.Port}{new string('\t', int.Parse(I18n.Strings.PortTabRepeat))}: {ServerConfig.Port}
{I18n.Strings.UserName}{new string('\t', int.Parse(I18n.Strings.UserNameTabRepeat))}: {ServerConfig.UserName}
{I18n.Strings.Password}{new string('\t', int.Parse(I18n.Strings.PasswordTabRepeat))}: {GetPasswordString(ServerConfig.Password, ShowServerPassword)}";

    private static string GetPasswordString(string origin, bool? show)
    {
        return show ?? false ? origin : "*********";
    }

    #endregion

    private readonly ConfigManager _configManager;

    public ServerConfigViewModel(ConfigManager configManager)
    {
        _configManager = configManager;
        _configManager.ListenConfig<ServerConfig>(config => ServerConfig = config);
        serverConfig = _configManager.GetConfig<ServerConfig>();
        serverEnable = serverConfig.SwitchOn;
        enableHttps = serverConfig.EnableHttps;
        certificatePemPath = serverConfig.CertificatePemPath;
        certificatePemKeyPath = serverConfig.CertificatePemKeyPath;
        enableCustomConfigurationFile = serverConfig.EnableCustomConfigurationFile;
        customConfigurationFilePath = serverConfig.CustomConfigurationFilePath;
        maxHistoryCount = serverConfig.MaxHistoryCount;
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
}