﻿using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public partial class SystemSettingViewModel : ObservableObject
{
    [ObservableProperty]
    private bool hideWindowOnStartUp;
    partial void OnHideWindowOnStartUpChanged(bool value) => ProgramConfig = ProgramConfig with { HideWindowOnStartup = value };

    [ObservableProperty]
    private bool diagnoseMode;
    partial void OnDiagnoseModeChanged(bool value) => ProgramConfig = ProgramConfig with { DiagnoseMode = value };

    [ObservableProperty]
    private uint logRemainDays;
    partial void OnLogRemainDaysChanged(uint value) => ProgramConfig = ProgramConfig with { LogRemainDays = value };

    [ObservableProperty]
    private uint tempFileRemainDays;
    partial void OnTempFileRemainDaysChanged(uint value) => ProgramConfig = ProgramConfig with { TempFileRemainDays = value };

    [ObservableProperty]
    private string font;
    partial void OnFontChanged(string value)
    {
        ProgramConfig = ProgramConfig with { Font = value };
        _services.GetRequiredService<IMainWindow>().SetFont(value);
    }

    public List<string> FontList
    {
        get
        {
            var font = _services.GetService<IFontManager>();
            if (font == null)
            {
                return new List<string>() { "" };
            }
            var list = font.GetInstalledFontNames();
            list.Insert(0, "");
            return list;
        }
    }

    [ObservableProperty]
    private ProgramConfig programConfig;
    partial void OnProgramConfigChanged(ProgramConfig value)
    {
        HideWindowOnStartUp = value.HideWindowOnStartup;
        LogRemainDays = value.LogRemainDays;
        TempFileRemainDays = value.TempFileRemainDays;
        DiagnoseMode = value.DiagnoseMode;
        Font = value.Font;
        Language = Languages.FirstOrDefault(x => x.LocaleTag == value.Language) ?? Languages[0];
        Theme = Themes.FirstOrDefault(x => x.String == ProgramConfig.Theme) ?? Themes[0];
        _configManager.SetConfig(value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangingLangInfo))]
    private LanguageModel language;
    partial void OnLanguageChanged(LanguageModel value) => ProgramConfig = ProgramConfig with { Language = value.LocaleTag };

    public static readonly LanguageModel[] Languages = I18nHelper.SupportedLanguage;
    public string DisplayMemberPath = nameof(LanguageModel.DisplayName);
    public string? ChangingLangInfo => I18nHelper.GetChangingLanguageInfo(Language);

    public static readonly LocaleString[] Themes =
    {
        new ("", Strings.SystemStyle),
        new ("Light", Strings.Light),
        new ("Dark", Strings.Dark)
    };

    [ObservableProperty]
    private LocaleString theme;
    partial void OnThemeChanged(LocaleString value)
    {
        ProgramConfig = ProgramConfig with { Theme = value.String };
        _services.GetRequiredService<IMainWindow>().ChangeTheme(value.String);
    }

    public static readonly LocaleString<bool>[] UserConfigPositions =
    {
        new (false, Strings.SystemRecommend),
        new (true, Strings.PrograminstallLocation)
    };

    [ObservableProperty]
    private LocaleString<bool> userConfigPosition;
    partial void OnUserConfigPositionChanged(LocaleString<bool> value)
    {
        _staticConfig.SetConfig(_staticConfig.GetConfig<EnvConfig>() with { PortableUserConfig = value.Key });
    }

    private void OnEnvConfigChanged(EnvConfig envConfig)
    {
        UserConfigPosition = LocaleString<bool>.Match(UserConfigPositions, envConfig.PortableUserConfig);
    }

    private readonly ConfigManager _configManager;
    private readonly StaticConfig _staticConfig;
    private readonly IServiceProvider _services;

    public SystemSettingViewModel(ConfigManager configManager, StaticConfig staticConfig, IServiceProvider serviceProvider)
    {
        _configManager = configManager;
        _staticConfig = staticConfig;
        _services = serviceProvider;

        _configManager.ListenConfig<ProgramConfig>(config => ProgramConfig = config);
        programConfig = _configManager.GetConfig<ProgramConfig>();
        language = Languages.FirstOrDefault(x => x.LocaleTag == programConfig.Language) ?? Languages[0];
        font = programConfig.Font;
        theme = Themes.FirstOrDefault(x => x.String == programConfig.Theme) ?? Themes[0];
        hideWindowOnStartUp = programConfig.HideWindowOnStartup;
        logRemainDays = programConfig.LogRemainDays;
        tempFileRemainDays = programConfig.TempFileRemainDays;
        diagnoseMode = programConfig.DiagnoseMode;

        _staticConfig.ListenConfig<EnvConfig>(OnEnvConfigChanged);
        var envConfig = _configManager.GetConfig<EnvConfig>();
        userConfigPosition = LocaleString<bool>.Match(UserConfigPositions, envConfig.PortableUserConfig);
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
