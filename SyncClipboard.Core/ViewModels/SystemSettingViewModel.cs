using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Notification;

namespace SyncClipboard.Core.ViewModels;

public partial class SystemSettingViewModel : ObservableObject
{
    public static string Version => "v" + Env.VERSION;

    [ObservableProperty]
    private bool checkUpdateOnStartUp;
    partial void OnCheckUpdateOnStartUpChanged(bool value) => ProgramConfig = ProgramConfig with { CheckUpdateOnStartUp = value };

    [ObservableProperty]
    private ProgramConfig programConfig;
    partial void OnProgramConfigChanged(ProgramConfig value)
    {
        CheckUpdateOnStartUp = value.CheckUpdateOnStartUp;
        _userConfig.SetConfig(ConfigKey.Program, value);
    }

    private readonly IServiceProvider _serviceProvider;
    private readonly UserConfig2 _userConfig;

    private UpdateChecker UpdateChecker => _serviceProvider.GetRequiredService<UpdateChecker>();
    private NotificationManager NotificationManager => _serviceProvider.GetRequiredService<NotificationManager>();

    public SystemSettingViewModel(IServiceProvider serviceProvider, UserConfig2 userConfig)
    {
        _serviceProvider = serviceProvider;
        _userConfig = userConfig;

        _userConfig.ListenConfig<ProgramConfig>(ConfigKey.Program, (config) => ProgramConfig = (config as ProgramConfig) ?? new());
        ProgramConfig = _userConfig.GetConfig<ProgramConfig>(ConfigKey.Program) ?? new();
    }

    public bool StartUpWithSystem
    {
        get => StartUpHelper.Status();
        set
        {
            StartUpHelper.Set(value);
            OnPropertyChanged(nameof(StartUpWithSystem));
        }
    }

    [RelayCommand]
    public async Task CheckForUpdate()
    {
        try
        {
            var (needUpdate, newVersion) = await UpdateChecker.Check();
            if (needUpdate)
            {
                NotificationManager.SendText(
                    "检测到新版本",
                    $"v{Env.VERSION} -> {newVersion}",
                    new Button("打开下载页面", () => Sys.OpenWithDefaultApp(UpdateChecker.ReleaseUrl))
                );
            }
            else
            {
                NotificationManager.SendText("当前版本为最新版本", $"本地版本v{Env.VERSION}，最新发布版本{newVersion}");
            }
        }
        catch (Exception ex)
        {
            NotificationManager.SendText("检查更新失败", ex.Message);
        }
    }
}
