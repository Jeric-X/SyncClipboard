using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public partial class SystemSettingViewModel : ObservableObject
{
    public static string Version => "v" + Env.VERSION;

    [ObservableProperty]
    private bool checkUpdateOnStartUp;
    partial void OnCheckUpdateOnStartUpChanged(bool value) => ProgramConfig = ProgramConfig with { CheckUpdateOnStartUp = value };

    [ObservableProperty]
    private uint logRemainDays;
    partial void OnLogRemainDaysChanged(uint value) => ProgramConfig = ProgramConfig with { LogRemainDays = value };

    [ObservableProperty]
    private ProgramConfig programConfig;
    partial void OnProgramConfigChanged(ProgramConfig value)
    {
        CheckUpdateOnStartUp = value.CheckUpdateOnStartUp;
        LogRemainDays = value.LogRemainDays;
        _configManager.SetConfig(ConfigKey.Program, value);
    }

    private readonly ConfigManager _configManager;

    public SystemSettingViewModel(ConfigManager configManager)
    {
        _configManager = configManager;

        _configManager.ListenConfig<ProgramConfig>(ConfigKey.Program, (config) => ProgramConfig = (config as ProgramConfig) ?? new());
        ProgramConfig = _configManager.GetConfig<ProgramConfig>(ConfigKey.Program) ?? new();
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
}
