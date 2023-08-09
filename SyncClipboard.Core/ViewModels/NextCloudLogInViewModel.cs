using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Notification;

namespace SyncClipboard.Core.ViewModels;

public class TreeViewModel
{
    public string Content;
    public List<TreeViewModel>? Children;

    public TreeViewModel(string content, List<TreeViewModel>? children = null)
    {
        Content = content;
        Children = children;
    }
}

public partial class NextCloudLogInViewModel : ObservableObject
{
    
    private readonly IServiceProvider _serviceProvider;

    private NotificationManager NotificationManager => _serviceProvider.GetRequiredService<NotificationManager>();
    private IHttp Http => _serviceProvider.GetRequiredService<IHttp>();

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

    public List<TreeViewModel> TreeList = new()
    {
        new TreeViewModel("level1_1", new List<TreeViewModel>(){ new TreeViewModel("level2") }),
        new TreeViewModel("level1_2")
    };

    [ObservableProperty]
    private string userInputUrl;

    public bool CanCancel => _cancelSource is not null && !_cancelSource.IsCancellationRequested;

    [ObservableProperty]
    private bool showProgressBar = false;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel()
    {
        if (CanCancel)
        {
            _cancelSource?.Cancel();
            _cancelSource = null;
        }
    }

    [RelayCommand]
    public async Task ComfirmUrl()
    {
        CancelSource = new CancellationTokenSource();
        ShowProgressBar = true;
        try
        {
            var loginFlow = new NextcloudLogInFlow(UserInputUrl, Http);
            var userloginUrl = await loginFlow.GetUserLoginUrl(CancelSource.Token);
            Sys.OpenWithDefaultApp(userloginUrl);
            var webDavCredential = await loginFlow.WaitUserLogin(CancelSource.Token);
            ShowProgressBar = false;
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            NotificationManager.SendText("认证中发生错误", GetErrorMessage(ex));
        }
        finally
        {
            ShowProgressBar = false;
            CancelSource = null;
        }
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
}
