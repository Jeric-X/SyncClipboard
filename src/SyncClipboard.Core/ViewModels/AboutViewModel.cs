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
    // Loading saved values must not persist partial settings or invoke platform services.
    private readonly bool _isInitializing = true;

    public string Version => _appConfig.AppVersion;

    private readonly ConfigManager _configManager;
    private readonly UpdateChecker _updateChecker;
    private readonly IAppConfig _appConfig;

    [ObservableProperty]
    public partial bool CheckUpdateOnStartUp { get; set; }

    partial void OnCheckUpdateOnStartUpChanged(bool value)
    {
        if (_isInitializing) return;

        _configManager.SetConfig(_configManager.GetConfig<ProgramConfig>() with { CheckUpdateOnStartUp = value });
    }

    [ObservableProperty]
    public partial bool AutoDownloadUpdate { get; set; }

    partial void OnAutoDownloadUpdateChanged(bool value)
    {
        if (_isInitializing) return;

        _configManager.SetConfig(_configManager.GetConfig<ProgramConfig>() with { AutoDownloadUpdate = value });
    }

    [ObservableProperty]
    public partial bool CheckUpdateForBeta { get; set; }

    partial void OnCheckUpdateForBetaChanged(bool value)
    {
        if (_isInitializing) return;

        _configManager.SetConfig(_configManager.GetConfig<ProgramConfig>() with { CheckUpdateForBeta = value });
    }

    public AboutViewModel(ConfigManager configManager, IAppConfig appConfig, UpdateChecker updateChecker)
    {
        _configManager = configManager;
        _appConfig = appConfig;
        _updateChecker = updateChecker;
        CheckUpdateOnStartUp = configManager.GetConfig<ProgramConfig>().CheckUpdateOnStartUp;
        CheckUpdateForBeta = configManager.GetConfig<ProgramConfig>().CheckUpdateForBeta;
        AutoDownloadUpdate = configManager.GetConfig<ProgramConfig>().AutoDownloadUpdate;

        _isInitializing = false;

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
        new OpenSourceSoftware("AsyncImageLoader.Avalonia", "https://github.com/AvaloniaUtils/AsyncImageLoader.Avalonia", "AsyncImageLoader.Avalonia/LICENSE.txt"),
        new OpenSourceSoftware("Vanara", "https://github.com/dahall/Vanara", "Vanara/LICENSE.txt"),
        new OpenSourceSoftware("SharpHook", "https://github.com/TolikPylypchuk/SharpHook", "SharpHook/LICENSE.txt"),
        new OpenSourceSoftware("Quartz.NET", "https://www.quartz-scheduler.net/", "quartznet/license.txt"),
        new OpenSourceSoftware("Hugeicons Free Stroke Rounded", "https://hugeicons.com/", "Hugeicons.Free.StrokeRounded.NOTICE.md"),
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
        public partial bool ShowPannel { get; set; } = false;

        [ObservableProperty]
        public partial Severity Severity { get; set; } = Severity.Info;

        [ObservableProperty]
        public partial string Message { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string ExtraMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool EnableProgressbar { get; set; } = false;

        [ObservableProperty]
        public partial bool EnableActionButton { get; set; } = false;

        [ObservableProperty]
        public partial bool IsIndeterminate { get; set; } = false;

        [ObservableProperty]
        public partial double ProgressValue { get; set; } = 0;

        [ObservableProperty]
        public partial string ActionButtonText { get; set; } = string.Empty;

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
            UpdaterState.UpdateAvailableAtMarket => Severity.Warning,
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
