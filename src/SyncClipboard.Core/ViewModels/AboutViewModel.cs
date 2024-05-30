using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Updater;

namespace SyncClipboard.Core.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public static string HomePage => Env.HomePage;
    public IAppConfig AppConfig => _services.GetRequiredService<IAppConfig>();
    public string Version => AppConfig.AppVersion;

    private readonly IServiceProvider _services;
    private readonly ConfigManager _configManager;

    private GithubUpdater UpdateChecker => _services.GetRequiredService<GithubUpdater>();
    private string _updateUrl;

    [ObservableProperty]
    private bool checkUpdateOnStartUp;
    partial void OnCheckUpdateOnStartUpChanged(bool value)
    {
        _configManager.SetConfig(_configManager.GetConfig<ProgramConfig>() with { CheckUpdateOnStartUp = value });
    }

    [ObservableProperty]
    private bool checkUpdateForBeta;
    partial void OnCheckUpdateForBetaChanged(bool value)
    {
        _configManager.SetConfig(_configManager.GetConfig<ProgramConfig>() with { CheckUpdateForBeta = value });
    }

    public AboutViewModel(ConfigManager configManager, IServiceProvider serviceProvider)
    {
        _services = serviceProvider;
        _configManager = configManager;
        if (_alreadyCheckedUpdate is false)
        {
            CheckForUpdateCommand.ExecuteAsync(null);
        }
        checkUpdateOnStartUp = configManager.GetConfig<ProgramConfig>().CheckUpdateOnStartUp;
        checkUpdateForBeta = configManager.GetConfig<ProgramConfig>().CheckUpdateForBeta;
        configManager.ListenConfig<ProgramConfig>(config => { CheckUpdateOnStartUp = config.CheckUpdateOnStartUp; });

        _updateUrl = AppConfig.UpdateUrl;
    }

    public OpenSourceSoftware SyncClipboard { get; } = new(I18n.Strings.SoftwareHomePage, Env.HomePage, "");

#if DEBUG
    private static bool _alreadyCheckedUpdate = true;
#else           
    private static bool _alreadyCheckedUpdate = false;
#endif

    private static string _updateInfo = I18n.Strings.CheckingUpdate;
    public string UpdateInfo
    {
        get => _updateInfo;
        private set
        {
            _updateInfo = value;
            OnPropertyChanged(nameof(UpdateInfo));
        }
    }

    public List<OpenSourceSoftware> Dependencies { get; } = new()
    {
        new OpenSourceSoftware("Magick.NET", "https://github.com/dlemstra/Magick.NET", "Magick.NET/License.txt"),
        new OpenSourceSoftware(".NET Community Toolkit", "https://github.com/CommunityToolkit/dotnet", ".NETCommunityToolkit/License.md"),
        new OpenSourceSoftware("H.NotifyIcon", "https://github.com/HavenDV/H.NotifyIcon", "H.NotifyIcon/LICENSE.md"),
        new OpenSourceSoftware("WinUIEx", "https://github.com/dotMorten/WinUIEx", "WinUIEx/LICENSE.txt"),
        new OpenSourceSoftware("moq", "https://github.com/moq/moq", "moq/License.txt"),
        new OpenSourceSoftware("Avalonia", "https://avaloniaui.net/", "Avalonia/licence.md"),
        new OpenSourceSoftware("FluentAvalonia", "https://github.com/amwx/FluentAvalonia/", "FluentAvalonia/LICENSE.txt"),
        new OpenSourceSoftware("FluentAvalonia.BreadcrumbBar", "https://github.com/indigo-san/FluentAvalonia.BreadcrumbBar", "FluentAvalonia.BreadcrumbBar/LICENSE.txt"),
        new OpenSourceSoftware("Vanara", "https://github.com/dahall/Vanara", "Vanara/LICENSE.txt"),
        new OpenSourceSoftware("Tmds.DBus", "https://github.com/tmds/Tmds.DBus", "Tmds.DBus/LICENSE.txt"),
        new OpenSourceSoftware("SharpHook", "https://github.com/TolikPylypchuk/SharpHook", "SharpHook/LICENSE.txt"),
        new OpenSourceSoftware("DotNetZip.Semverd", "https://github.com/haf/DotNetZip.Semverd", "DotNetZip.Semverd/LICENSE.txt"),
    };

    [RelayCommand]
    public void OpenUpdateUrl()
    {
        Sys.OpenWithDefaultApp(_updateUrl);
    }

    [RelayCommand]
    public async Task CheckForUpdate()
    {
        _alreadyCheckedUpdate = true;
        UpdateInfo = I18n.Strings.CheckingUpdate;
        try
        {
            var (needUpdate, githubRelease) = await UpdateChecker.Check();
            _updateUrl = githubRelease.HtmlUrl!;
            if (needUpdate)
            {
                UpdateInfo = $"{I18n.Strings.FoundNewVersion}\nv{UpdateChecker.Version} -> {githubRelease.TagName}";
            }
            else
            {
                UpdateInfo = $"{I18n.Strings.ItsLatestVersion}\n{string.Format(I18n.Strings.SoftwareUpdateInfo, UpdateChecker.Version, githubRelease.TagName)}";
            }
        }
        catch
        {
            UpdateInfo = I18n.Strings.FailedToCheck;
        }
    }
}
