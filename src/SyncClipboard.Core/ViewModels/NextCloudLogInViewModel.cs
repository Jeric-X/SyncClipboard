using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer.Adapter.WebDavAdapter;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Web;

namespace SyncClipboard.Core.ViewModels;

public partial class NextCloudLogInViewModel(IServiceProvider serviceProvider) : ObservableObject
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private INotificationManager NotificationManager => _serviceProvider.GetRequiredService<INotificationManager>();
    private ConfigManager ConfigManager => _serviceProvider.GetRequiredService<ConfigManager>();
    private AccountManager AccountManager => _serviceProvider.GetRequiredService<AccountManager>();
    private IAppConfig AppConfig => _serviceProvider.GetRequiredService<IAppConfig>();

    private CancellationTokenSource? _cancelSource;
    private CancellationTokenSource? CancelSource
    {
        get => _cancelSource;
        set
        {
            _cancelSource = value;
            OnPropertyChanged(nameof(CanCancel));
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTreeList))]
    public List<FileTreeViewModel>? treeList;

    public bool ShowTreeList => TreeList is not null;

    [ObservableProperty]
    private string userInputUrl = "https://";

    public bool CanCancel => CancelSource is not null && !CancelSource.IsCancellationRequested;

    [ObservableProperty]
    private bool showProgressBar = false;

    [ObservableProperty]
    private bool showFolderProgressBar = false;

    private WebDav? _tempWebDav;
    private WebDavCredential? tempWebDavCredential;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel()
    {
        if (CanCancel)
        {
            CancelSource?.Cancel();
            CancelSource = null;
        }
    }

    [RelayCommand]
    public async Task ComfirmUrl()
    {
        CancelSource = new CancellationTokenSource();
        ShowProgressBar = true;
        TreeList = null;
        try
        {
            var insecure = ConfigManager.GetConfig<SyncConfig>().TrustInsecureCertificate;
            using var httpClient = new HttpClientFactory { TrustInsecureCertificate = insecure }.CreateClient();
            var loginFlow = new NextcloudLogInFlow(UserInputUrl, httpClient);
            var userloginUrl = await loginFlow.GetUserLoginUrl(CancelSource.Token);
            Sys.OpenWithDefaultApp(userloginUrl);
            tempWebDavCredential = await loginFlow.WaitUserLogin(CancelSource.Token);
            ShowProgressBar = false;
            _tempWebDav = CreateTempWebDav(tempWebDavCredential, insecure);
            TreeList = WebDavModelToViewModel(await _tempWebDav.GetFolderSubList(""));
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            NotificationManager.ShowText(I18n.Strings.FailedToAuth, GetErrorMessage(ex));
        }
        finally
        {
            ShowProgressBar = false;
            CancelSource = null;
        }
    }

    [RelayCommand]
    public void SetFolder(FileTreeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(tempWebDavCredential);

        var webDavConfig = new WebDavConfig() with
        {
            UserName = tempWebDavCredential.Username,
            Password = tempWebDavCredential.Password,
            RemoteURL = $"{tempWebDavCredential.Url.Trim('/')}/{node.FullPath.Trim('/')}",
        };

        var accountId = AccountManager.CreateAccountId(WebDavConfig.TYPE_NAME);
        AccountManager.SetConfig(accountId, WebDavConfig.TYPE_NAME, webDavConfig);

        _serviceProvider.GetRequiredService<MainViewModel>().NavigateToLastLevel();
    }

    public async Task SetChildren(FileTreeViewModel? node)
    {
        if (_tempWebDav is null || node is null || node.Children is not null)
        {
            return;
        }
        ShowFolderProgressBar = true;
        var delayTask = Task.Delay(500);
        node.Children = WebDavModelToViewModel(await _tempWebDav.GetFolderSubList(node.FullPath));
        await delayTask;
        ShowFolderProgressBar = false;
    }

    [RelayCommand]
    public async Task Refresh()
    {
        TreeList = WebDavModelToViewModel(await _tempWebDav!.GetFolderSubList(""));
    }

    private static string GetErrorMessage(Exception ex)
    {
        return ex switch
        {
            HttpRequestException => "Can not connect to the server",
            UriFormatException => "URL format is wrong",
            _ => ex.Message,
        };
    }

    private static List<FileTreeViewModel> WebDavModelToViewModel(List<WebDavNode> webDavNodes)
    {
        return webDavNodes
            .Where(x => x.IsFolder)
            .Select(x => new FileTreeViewModel(x.FullPath, x.Name, x.IsFolder))
            .ToList();
    }

    private WebDav CreateTempWebDav(WebDavCredential tempWebDavCredential, bool insecure)
    {
        _tempWebDav?.Dispose();
        _tempWebDav = new WebDav(tempWebDavCredential, AppConfig, insecure);
        return _tempWebDav;
    }
}
