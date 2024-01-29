using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Web;

namespace SyncClipboard.Core.ViewModels;

public partial class FileTreeViewModel : ObservableObject
{
    public string FullPath { get; }
    public string Name { get; }
    public bool IsFolder { get; }

    [ObservableProperty]
    public List<FileTreeViewModel>? children;

    public FileTreeViewModel(string fullPath, string name, bool isFolder)
    {
        FullPath = fullPath;
        Name = name;
        IsFolder = isFolder;
    }
}

public partial class NextCloudLogInViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private INotification NotificationManager => _serviceProvider.GetRequiredService<INotification>();
    private IHttp Http => _serviceProvider.GetRequiredService<IHttp>();
    private ConfigManager ConfigManager => _serviceProvider.GetRequiredService<ConfigManager>();
    private IAppConfig AppConfig => _serviceProvider.GetRequiredService<IAppConfig>();

    public NextCloudLogInViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        userInputUrl = "https://";
    }

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
    private string userInputUrl;

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
            var loginFlow = new NextcloudLogInFlow(UserInputUrl, Http);
            var userloginUrl = await loginFlow.GetUserLoginUrl(CancelSource.Token);
            Sys.OpenWithDefaultApp(userloginUrl);
            tempWebDavCredential = await loginFlow.WaitUserLogin(CancelSource.Token);
            ShowProgressBar = false;
            _tempWebDav = new WebDav(tempWebDavCredential, AppConfig);
            TreeList = WebDavModelToViewModel(await _tempWebDav.GetFolderSubList(""));
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            NotificationManager.SendText(I18n.Strings.FailedToAuth, GetErrorMessage(ex));
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
        var config = ConfigManager.GetConfig<SyncConfig>();
        ConfigManager.SetConfig(
            config with
            {
                UserName = tempWebDavCredential.Username,
                Password = tempWebDavCredential.Password,
                RemoteURL = $"{tempWebDavCredential.Url.Trim('/')}/{node.FullPath.Trim('/')}"
            }
        );
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
}
