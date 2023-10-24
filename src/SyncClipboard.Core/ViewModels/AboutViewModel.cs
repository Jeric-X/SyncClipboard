using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public static string HomePage => Env.HomePage;
    public IAppConfig AppConfig => _services.GetRequiredService<IAppConfig>();
    public string Version => AppConfig.AppVersion;

    private readonly IServiceProvider _services;

    private UpdateChecker UpdateChecker => _services.GetRequiredService<UpdateChecker>();

    public AboutViewModel(IServiceProvider serviceProvider)
    {
        _services = serviceProvider;
        if (_alreadyCheckedUpdate is false)
        {
            CheckForUpdateCommand.ExecuteAsync(null);
        }
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
        new OpenSourceSoftware("Windows Community ToolkitLabs", "https://github.com/CommunityToolkit/Labs-Windows", "WindowsCommunityToolkitLabs/License.md"),
        new OpenSourceSoftware(".NET Community Toolkit", "https://github.com/CommunityToolkit/dotnet", ".NETCommunityToolkit/License.md"),
        new OpenSourceSoftware("H.NotifyIcon", "https://github.com/HavenDV/H.NotifyIcon", "H.NotifyIcon/LICENSE.md"),
        new OpenSourceSoftware("WinUIEx", "https://github.com/dotMorten/WinUIEx", "WinUIEx/LICENSE.txt"),
        new OpenSourceSoftware("moq", "https://github.com/moq/moq", "moq/License.txt"),
    };

    [RelayCommand]
    public void OpenUpdateUrl()
    {
        Sys.OpenWithDefaultApp(AppConfig.UpdateUrl);
    }

    [RelayCommand]
    public async Task CheckForUpdate()
    {
        _alreadyCheckedUpdate = true;
        UpdateInfo = I18n.Strings.CheckingUpdate;
        try
        {
            var (needUpdate, newVersion) = await UpdateChecker.Check();
            if (needUpdate)
            {
                UpdateInfo = $"{I18n.Strings.FoundNewVersion}\nv{UpdateChecker.Version} -> {newVersion}";
            }
            else
            {
                UpdateInfo = $"{I18n.Strings.ItsLatestVersion}\n{string.Format(I18n.Strings.SoftwareUpdateInfo, UpdateChecker.Version, newVersion)}";
            }
        }
        catch
        {
            UpdateInfo = I18n.Strings.FailedToCheck;
        }
    }
}
