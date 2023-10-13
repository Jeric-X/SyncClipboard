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
        Language = Languages.FirstOrDefault(x => x.Tag == value.Language) ?? Languages[0];
        _configManager.SetConfig(ConfigKey.Program, value);
    }

    [ObservableProperty]
    private LanguageDefine language;
    partial void OnLanguageChanged(LanguageDefine value) => ProgramConfig = ProgramConfig with { Language = value.Tag };

    private const string DefaultLangTag = "Default";
    public record class LanguageDefine
    {
        public string LocalName { get; }
        public string Tag { get; }
        public string DisplayName => Tag switch
        {
            DefaultLangTag => LocalName,
            _ => $"{LocalName} ({Tag})"
        };
        public LanguageDefine(string localName, string tag)
        {
            LocalName = localName;
            Tag = tag;
        }
    }
    public static List<LanguageDefine> Languages => new()
    {
        new (I18n.Strings.DefaultLanguage, DefaultLangTag),
        new("English", "en-US"),
        new("简体中文", "zh-CN"),
    };

    private readonly ConfigManager _configManager;

    public SystemSettingViewModel(ConfigManager configManager)
    {
        _configManager = configManager;

        _configManager.ListenConfig<ProgramConfig>(ConfigKey.Program, (config) => ProgramConfig = (config as ProgramConfig) ?? new());
        ProgramConfig = _configManager.GetConfig<ProgramConfig>(ConfigKey.Program) ?? new();
        language = Languages.FirstOrDefault(x => x.Tag == ProgramConfig.Language) ?? Languages[0];
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
