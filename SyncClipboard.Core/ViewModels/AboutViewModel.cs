using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public static string HomePage => Env.HomePage;

    private readonly IServiceProvider _serviceProvider;

    private UpdateChecker UpdateChecker => _serviceProvider.GetRequiredService<UpdateChecker>();

    public AboutViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        if (_checkedUpdate is false)
        {
            CheckForUpdateCommand.ExecuteAsync(null);
        }
    }

    public OpenSourceSoftware SyncClipboard { get; } = new("软件地址", Env.HomePage, "");

    // TODO：change this to false
    private static bool _checkedUpdate = true;
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
        new OpenSourceSoftware("Magick.NET", "https://github.com/dlemstra/Magick.NET","License.txt"),
        new OpenSourceSoftware("Windows Community Toolkit Labs", "https://github.com/CommunityToolkit/Labs-Windows","License.md"),
        new OpenSourceSoftware(".NET Community Toolkit", "https://github.com/CommunityToolkit/dotnet","License.md"),
        new OpenSourceSoftware("H.NotifyIcon", "https://github.com/HavenDV/H.NotifyIcon","LICENSE.md"),
        new OpenSourceSoftware("WinUIEx", "https://github.com/dotMorten/WinUIEx","LICENSE.txt"),
        new OpenSourceSoftware("moq", "https://github.com/moq/moq","License.txt"),
    };

    [RelayCommand]
    public static void OpenUpdateUrl()
    {
        Sys.OpenWithDefaultApp(UpdateChecker.ReleaseUrl);
    }

    [RelayCommand]
    public async Task CheckForUpdate()
    {
        _checkedUpdate = true;
        UpdateInfo = I18n.Strings.CheckingUpdate;
        try
        {
            var (needUpdate, newVersion) = await UpdateChecker.Check();
            if (needUpdate)
            {
                UpdateInfo = $"{I18n.Strings.FoundNewVersion}\nv{Env.VERSION} -> {newVersion}";
            }
            else
            {
                UpdateInfo = $"{I18n.Strings.ItsLatestVersion}\n{string.Format(I18n.Strings.SoftwareUpdateInfo, Env.VERSION, newVersion)}";
            }
        }
        catch
        {
            UpdateInfo = I18n.Strings.FailedToCheck;
        }
    }
}
