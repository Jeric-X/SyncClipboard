using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public partial class SystemSettingViewModel : ObservableObject
{
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
        Language = Languages.FirstOrDefault(x => x.LocaleTag == value.Language) ?? Languages[0];
        _configManager.SetConfig(ConfigKey.Program, value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangingLangInfo))]
    private LanguageModel language;
    partial void OnLanguageChanged(LanguageModel value) => ProgramConfig = ProgramConfig with { Language = value.LocaleTag };

    public static readonly LanguageModel[] Languages = I18nHelper.SupportedLanguage;
    public string DisplayMemberPath = nameof(LanguageModel.DisplayName);
    public string? ChangingLangInfo => I18nHelper.GetChangingLanguageInfo(Language);

    private readonly ConfigManager _configManager;

    public SystemSettingViewModel(ConfigManager configManager)
    {
        _configManager = configManager;

        _configManager.ListenConfig<ProgramConfig>(ConfigKey.Program, (config) => ProgramConfig = (config as ProgramConfig) ?? new());
        ProgramConfig = _configManager.GetConfig<ProgramConfig>(ConfigKey.Program) ?? new();
        language = Languages.FirstOrDefault(x => x.LocaleTag == ProgramConfig.Language) ?? Languages[0];
        checkUpdateOnStartUp = ProgramConfig.CheckUpdateOnStartUp;
        logRemainDays = ProgramConfig.LogRemainDays;
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
