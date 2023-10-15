using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Notification;

namespace SyncClipboard.Core.ViewModels;

public partial class AboutViewModel
{
    public static string HomePage => Env.HomePage;

    private readonly IServiceProvider _serviceProvider;

    private UpdateChecker UpdateChecker => _serviceProvider.GetRequiredService<UpdateChecker>();
    private NotificationManager NotificationManager => _serviceProvider.GetRequiredService<NotificationManager>();

    public AboutViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public List<OpenSourceSoftware> Dependencies = new()
    {
        new OpenSourceSoftware("Magick.NET", "https://github.com/dlemstra/Magick.NET","License.txt"),
        new OpenSourceSoftware("Windows Community Toolkit Labs", "https://github.com/CommunityToolkit/Labs-Windows","License.md"),
        new OpenSourceSoftware(".NET Community Toolkit", "https://github.com/CommunityToolkit/dotnet","License.md"),
        new OpenSourceSoftware("H.NotifyIcon", "https://github.com/HavenDV/H.NotifyIcon","LICENSE.md"),
        new OpenSourceSoftware("WinUIEx", "https://github.com/dotMorten/WinUIEx","LICENSE.txt"),
        new OpenSourceSoftware("moq", "https://github.com/moq/moq","License.txt"),
    };

    [RelayCommand]
    public async Task CheckForUpdate()
    {
        try
        {
            var (needUpdate, newVersion) = await UpdateChecker.Check();
            if (needUpdate)
            {
                NotificationManager.SendText(
                    I18n.Strings.FoundNewVersion,
                    $"v{Env.VERSION} -> {newVersion}",
                    new Button(I18n.Strings.OpenDownloadPage, () => Sys.OpenWithDefaultApp(UpdateChecker.ReleaseUrl))
                );
            }
            else
            {
                NotificationManager.SendText(
                    I18n.Strings.ItsLatestVersion,
                    string.Format(I18n.Strings.SoftwareUpdateInfo, Env.VERSION, newVersion));
            }
        }
        catch (Exception ex)
        {
            NotificationManager.SendText(I18n.Strings.FailedToCheck, ex.Message);
        }
    }
}
