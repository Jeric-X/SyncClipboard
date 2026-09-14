using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public partial class ServerConfigViewModel : ObservableObject
{
    // Loading saved values must not persist partial settings or invoke platform services.
    private readonly bool _isInitializing = true;

    #region server properties
    [ObservableProperty]
    public partial bool ServerEnable { get; set; }

    partial void OnServerEnableChanged(bool value)
    {
        if (_isInitializing) return;
        ServerConfig = ServerConfig with { SwitchOn = value };
    }

    [ObservableProperty]
    public partial bool EnableHttps { get; set; }

    partial void OnEnableHttpsChanged(bool value)
    {
        if (_isInitializing) return;
        ServerConfig = ServerConfig with { EnableHttps = value };
    }

    public static readonly IEnumerable<string> CertificatePemFileTypes = [".pem"];
    [ObservableProperty]
    public partial string CertificatePemPath { get; set; } = string.Empty;

    partial void OnCertificatePemPathChanged(string value)
    {
        if (_isInitializing) return;
        ServerConfig = ServerConfig with { CertificatePemPath = value };
    }

    public static readonly IEnumerable<string> CertificatePemKeyFileTypes = [".pem"];
    [ObservableProperty]
    public partial string CertificatePemKeyPath { get; set; } = string.Empty;

    partial void OnCertificatePemKeyPathChanged(string value)
    {
        if (_isInitializing) return;
        ServerConfig = ServerConfig with { CertificatePemKeyPath = value };
    }

    [ObservableProperty]
    public partial bool EnableCustomConfigurationFile { get; set; }

    partial void OnEnableCustomConfigurationFileChanged(bool value)
    {
        if (_isInitializing) return;
        ServerConfig = ServerConfig with { EnableCustomConfigurationFile = value };
    }

    public static readonly IEnumerable<string> CustomConfigurationFileTypes = [".json"];
    [ObservableProperty]
    public partial string CustomConfigurationFilePath { get; set; } = string.Empty;

    partial void OnCustomConfigurationFilePathChanged(string value)
    {
        if (_isInitializing) return;
        ServerConfig = ServerConfig with { CustomConfigurationFilePath = value };
    }

    [ObservableProperty]
    public partial uint MaxHistoryCount { get; set; }

    partial void OnMaxHistoryCountChanged(uint value)
    {
        if (_isInitializing) return;
        ServerConfig = ServerConfig with { MaxHistoryCount = value };
    }

    [ObservableProperty]
    public partial uint HistoryRetentionMinutes { get; set; }

    partial void OnHistoryRetentionMinutesChanged(uint value)
    {
        if (_isInitializing) return;
        ServerConfig = ServerConfig with { HistoryRetentionMinutes = value };
    }

    [RelayCommand]
    private static void OpenCustomConfigDescLink()
    {
        Sys.OpenWithDefaultApp(I18n.Strings.CustomConfigFileLink);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerConfigDescription))]
    public partial ServerConfig ServerConfig { get; set; } = new();

    partial void OnServerConfigChanged(ServerConfig value)
    {
        if (_isInitializing) return;

        ServerEnable = value.SwitchOn;
        EnableHttps = value.EnableHttps;
        CertificatePemPath = value.CertificatePemPath;
        CertificatePemKeyPath = value.CertificatePemKeyPath;
        EnableCustomConfigurationFile = value.EnableCustomConfigurationFile;
        CustomConfigurationFilePath = value.CustomConfigurationFilePath;
        MaxHistoryCount = value.MaxHistoryCount;
        HistoryRetentionMinutes = value.HistoryRetentionMinutes;
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
    public partial bool ShowServerPassword { get; set; } = false;

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
        ServerConfig = _configManager.GetConfig<ServerConfig>();
        ServerEnable = ServerConfig.SwitchOn;
        EnableHttps = ServerConfig.EnableHttps;
        CertificatePemPath = ServerConfig.CertificatePemPath;
        CertificatePemKeyPath = ServerConfig.CertificatePemKeyPath;
        EnableCustomConfigurationFile = ServerConfig.EnableCustomConfigurationFile;
        CustomConfigurationFilePath = ServerConfig.CustomConfigurationFilePath;
        MaxHistoryCount = ServerConfig.MaxHistoryCount;
        HistoryRetentionMinutes = ServerConfig.HistoryRetentionMinutes;
        _isInitializing = false;
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