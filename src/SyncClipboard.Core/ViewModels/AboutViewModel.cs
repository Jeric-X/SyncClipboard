using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Updater;

namespace SyncClipboard.Core.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public string Version => _appConfig.AppVersion;

    private readonly ConfigManager _configManager;
    private readonly UpdateChecker _updateChecker;
    private readonly IAppConfig _appConfig;

    [ObservableProperty]
    private bool checkUpdateOnStartUp;
    partial void OnCheckUpdateOnStartUpChanged(bool value)
    {
        _configManager.SetConfig(_configManager.GetConfig<ProgramConfig>() with { CheckUpdateOnStartUp = value });
    }

    [ObservableProperty]
    private bool autoDownloadUpdate;
    partial void OnAutoDownloadUpdateChanged(bool value)
    {
        _configManager.SetConfig(_configManager.GetConfig<ProgramConfig>() with { AutoDownloadUpdate = value });
    }

    [ObservableProperty]
    private bool checkUpdateForBeta;
    partial void OnCheckUpdateForBetaChanged(bool value)
    {
        _configManager.SetConfig(_configManager.GetConfig<ProgramConfig>() with { CheckUpdateForBeta = value });
    }

    public AboutViewModel(ConfigManager configManager, IAppConfig appConfig, UpdateChecker updateChecker)
    {
        _configManager = configManager;
        _appConfig = appConfig;
        _updateChecker = updateChecker;

        checkUpdateOnStartUp = configManager.GetConfig<ProgramConfig>().CheckUpdateOnStartUp;
        checkUpdateForBeta = configManager.GetConfig<ProgramConfig>().CheckUpdateForBeta;
        autoDownloadUpdate = configManager.GetConfig<ProgramConfig>().AutoDownloadUpdate;

        configManager.ListenConfig<ProgramConfig>(config =>
        {
            CheckUpdateOnStartUp = config.CheckUpdateOnStartUp;
            CheckUpdateForBeta = config.CheckUpdateForBeta;
            AutoDownloadUpdate = config.AutoDownloadUpdate;
        });

        _updateChecker.DownloadProgressChanged += DownloadProgressChanged;
        _updateChecker.StateChanged += UpdateStateChanged;
        UpdateStateChanged(_updateChecker.CurrentState);
    }

    public List<OpenSourceSoftware> Dependencies { get; } =
    [
        new OpenSourceSoftware("NativeNotification", "https://github.com/Jeric-X/NativeNotification", "NativeNotification/LICENSE.txt"),
        new OpenSourceSoftware("Magick.NET", "https://github.com/dlemstra/Magick.NET", "Magick.NET/License.txt"),
        new OpenSourceSoftware(".NET Community Toolkit", "https://github.com/CommunityToolkit/dotnet", "NETCommunityToolkit/License.md"),
        new OpenSourceSoftware("H.NotifyIcon", "https://github.com/HavenDV/H.NotifyIcon", "H.NotifyIcon/LICENSE.md"),
        new OpenSourceSoftware("WinUIEx", "https://github.com/dotMorten/WinUIEx", "WinUIEx/LICENSE.txt"),
        new OpenSourceSoftware("moq", "https://github.com/moq/moq", "moq/License.txt"),
        new OpenSourceSoftware("Avalonia", "https://avaloniaui.net/", "Avalonia/licence.md"),
        new OpenSourceSoftware("FluentAvalonia", "https://github.com/amwx/FluentAvalonia/", "FluentAvalonia/LICENSE.txt"),
        new OpenSourceSoftware("FluentAvalonia.BreadcrumbBar", "https://github.com/indigo-san/FluentAvalonia.BreadcrumbBar", "FluentAvalonia.BreadcrumbBar/LICENSE.txt"),
        new OpenSourceSoftware("AsyncImageLoader.Avalonia", "https://github.com/AvaloniaUtils/AsyncImageLoader.Avalonia", "AsyncImageLoader.Avalonia/LICENSE.txt"),
        new OpenSourceSoftware("Vanara", "https://github.com/dahall/Vanara", "Vanara/LICENSE.txt"),
        new OpenSourceSoftware("Tmds.DBus", "https://github.com/tmds/Tmds.DBus", "Tmds.DBus/LICENSE.txt"),
        new OpenSourceSoftware("SharpHook", "https://github.com/TolikPylypchuk/SharpHook", "SharpHook/LICENSE.txt"),
        new OpenSourceSoftware("Quartz.NET", "https://www.quartz-scheduler.net/", "quartznet/license.txt"),
#if LINUX
        new OpenSourceSoftware("MiSans Font", "https://hyperos.mi.com/font", string.Empty),
#endif
    ];

    [RelayCommand]
    public static void OpenHomePage()
    {
        Sys.OpenWithDefaultApp(Env.HomePage);
    }

    [RelayCommand]
    public void OpenReleasePage()
    {
        Sys.OpenWithDefaultApp(CheckUpdateForBeta ? _appConfig.UpdateUrl : _appConfig.UpdateUrl + "/latest");
    }

    public partial class UpdateStatusViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool showPannel = false;
        [ObservableProperty]
        private Severity severity = Severity.Info;
        [ObservableProperty]
        private string message = string.Empty;
        [ObservableProperty]
        private string extraMessage = string.Empty;
        [ObservableProperty]
        private bool enableProgressbar = false;
        [ObservableProperty]
        private bool enableActionButton = false;
        [ObservableProperty]
        private bool isIndeterminate = false;
        [ObservableProperty]
        private double progressValue = 0;
        [ObservableProperty]
        private string actionButtonText = string.Empty;
        public Func<CancellationToken, Task>? Action;
        [RelayCommand]
        private void RunAction()
        {
            if (Action is not null)
            {
                Action(CancellationToken.None);
            }
        }
    }

    public UpdateStatusViewModel UpdateStatus { get; } = new();

    private void UpdateStateChanged(UpdaterStatus status)
    {
        UpdateStatus.ShowPannel = status.State != UpdaterState.Idle;
        UpdateStatus.Message = status.Message;
        UpdateStatus.ExtraMessage = string.Empty;
        UpdateStatus.EnableProgressbar = status.State is UpdaterState.Downloading or UpdaterState.CheckingForUpdate;
        UpdateStatus.IsIndeterminate = true;
        UpdateStatus.ProgressValue = 0;

        UpdateStatus.ActionButtonText = status.ActionText;
        UpdateStatus.Action = status.ManualAction;
        UpdateStatus.EnableActionButton = status.ManualAction is not null && !string.IsNullOrEmpty(status.ActionText);

        if (status.State is UpdaterState.Downloading)
        {
            DownloadProgressChanged(_updateChecker.DownloadProgress);
        }

        if (status.State is UpdaterState.Failed)
        {
            UpdateStatus.ExtraMessage = status.Message;
            UpdateStatus.Message = I18n.Strings.Error;
        }

        UpdateStatus.Severity = status.State switch
        {
            UpdaterState.Idle => Severity.Info,
            UpdaterState.CheckingForUpdate => Severity.Info,
            UpdaterState.UpdateAvailable => Severity.Warning,
            UpdaterState.UpdateAvailableAt3rdPartySrc => Severity.Warning,
            UpdaterState.UpdateAvailableAtGitHubExtra => Severity.Warning,
            UpdaterState.ReadyForDownload => Severity.Warning,
            UpdaterState.UpToDate => Severity.Success,
            UpdaterState.Downloading => Severity.Info,
            UpdaterState.Downloaded => Severity.Warning,
            UpdaterState.Failed => Severity.Error,
            UpdaterState.Canceled => Severity.Warning,
            _ => Severity.Error
        };
    }

    private void DownloadProgressChanged(HttpDownloadProgress progress)
    {
        if (progress.End)
        {
            UpdateStatus.ProgressValue = 100;
            return;
        }

        if (progress.TotalBytesToReceive.HasValue)
        {
            UpdateStatus.ProgressValue = 100.0 * progress.BytesReceived / progress.TotalBytesToReceive.Value;
            UpdateStatus.IsIndeterminate = false;
        }
    }

    [RelayCommand]
    public async Task CheckForUpdate()
    {
        await _updateChecker.RunAutoUpdateFlow();
    }
}
