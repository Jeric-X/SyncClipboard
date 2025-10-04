using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.ViewModels;

public partial class AddAccountViewModel : ObservableObject
{
    private readonly ConfigManager _configManager;
    private readonly MainViewModel _mainVM;

    public AddAccountViewModel(ConfigManager configManager, MainViewModel mainViewModel)
    {
        _configManager = configManager;
        _mainVM = mainViewModel;
    }

    [ObservableProperty]
    private string serverUrl = "";

    [ObservableProperty]
    private string username = "";

    [ObservableProperty]
    private string password = "";

    [ObservableProperty]
    private bool isLoading = false;

    [RelayCommand]
    private async Task AddAccount()
    {
        // TODO: 实现添加账号逻辑
        IsLoading = true;
        try
        {
            // 这里将来会调用AccountManager来添加账号
            await Task.Delay(1000); // 模拟异步操作
            
            // 添加成功后返回
            _mainVM.NavigateToLastLevel();
        }
        catch
        {
            // TODO: 显示错误信息
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _mainVM.NavigateToLastLevel();
    }

    [RelayCommand]
    private void TestConnection()
    {
        // TODO: 实现测试连接逻辑
    }
}