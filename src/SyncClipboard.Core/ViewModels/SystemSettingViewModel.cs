using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using System.Diagnostics;
using System.IO;

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
                return [""];
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
        Theme = Themes.FirstOrDefault(x => x.Key == ProgramConfig.Theme) ?? Themes[0];
        _configManager.SetConfig(value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangingLangInfo))]
    private LanguageModel language;
    partial void OnLanguageChanged(LanguageModel value) => ProgramConfig = ProgramConfig with { Language = value.LocaleTag };

    public static readonly LanguageModel[] Languages = I18nHelper.SupportedLanguage;
    public string DisplayMemberPath = nameof(LanguageModel.DisplayName);
    public string? ChangingLangInfo => I18nHelper.GetChangingLanguageInfo(Language);

    public static readonly LocaleString<string>[] Themes =
    [
        new ("", Strings.SystemStyle),
        new ("Light", Strings.Light),
        new ("Dark", Strings.Dark)
    ];

    [ObservableProperty]
    private LocaleString<string> theme;
    partial void OnThemeChanged(LocaleString<string> value)
    {
        ProgramConfig = ProgramConfig with { Theme = value.Key };
    }

    public static readonly LocaleString<bool>[] UserConfigPositions =
    [
        new (false, Strings.SystemRecommend),
        new (true, Strings.PrograminstallLocation)
    ];

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
        theme = Themes.FirstOrDefault(x => x.Key == programConfig.Theme) ?? Themes[0];
        hideWindowOnStartUp = programConfig.HideWindowOnStartup;
        logRemainDays = programConfig.LogRemainDays;
        tempFileRemainDays = programConfig.TempFileRemainDays;
        diagnoseMode = programConfig.DiagnoseMode;

        _staticConfig.ListenConfig<EnvConfig>(OnEnvConfigChanged);
        var envConfig = _staticConfig.GetConfig<EnvConfig>();
        userConfigPosition = LocaleString<bool>.Match(UserConfigPositions, envConfig.PortableUserConfig);
    }

    public bool ShowStartUpSetting { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public static string AppDataDirectory => Env.AppDataDirectory;

    public static bool IsUsingCustomAppDataDirectory => Env.IsUsingCustomAppDataDirectory;

    [ObservableProperty]
    private bool isMovingAppData;

    [ObservableProperty]
    private string appDataMoveProgress = string.Empty;

    public bool StartUpWithSystem
    {
        get => StartUpHelper.Status();
        set
        {
            StartUpHelper.Set(value);
            OnPropertyChanged(nameof(StartUpWithSystem));
        }
    }

    /// <summary>
    /// Called by code-behind after a folder is picked by the user.
    /// Shows confirmation dialog, handles copying, error/success dialogs, and restart prompt.
    /// </summary>
    public async Task ChangeAppDataFolderAsync(string selectedFolder)
    {
        var targetFolder = await ConfirmAndResolveTargetFolderAsync(selectedFolder);
        if (targetFolder is null) return;

        if (Env.IsSamePath(targetFolder, Env.AppDataDirectory))
            return;

        var dialog = _services.GetRequiredService<IMainWindowDialog>();
        IsMovingAppData = true;
        string? error;
        try
        {
            error = await MoveAppDataFolderAsync(targetFolder);
        }
        finally
        {
            IsMovingAppData = false;
            AppDataMoveProgress = string.Empty;
        }

        if (error is not null)
        {
            await dialog.ShowMessageAsync(Strings.AppDataFolder, error);
            return;
        }

        await dialog.ShowMessageAsync(Strings.AppDataFolder, Strings.AppDataFolderCopySuccess);
        RestartApp();
    }

    /// <summary>
    /// Shows a confirmation dialog for creating a subfolder.
    /// Returns the target folder path, or null if cancelled.
    /// </summary>
    private async Task<string?> ConfirmAndResolveTargetFolderAsync(string selectedFolder)
    {
        var dialog = _services.GetRequiredService<IMainWindowDialog>();
        var withSubfolder = Path.Combine(selectedFolder, Env.SoftName);
        var message = string.Format(Strings.ChangeAppDataFolderConfirmMessage, withSubfolder, selectedFolder);
        var result = await dialog.ShowThreeButtonConfirmationAsync(
            Strings.ChangeAppDataFolderConfirmTitle,
            message,
            Strings.YesWithSubfolder,
            Strings.NoWithoutSubfolder,
            Strings.Cancel);

        if (result is null) return null;

        return result == true ? withSubfolder : selectedFolder;
    }

    /// <summary>
    /// Clears the custom app data path config so the app uses the default folder on next start.
    /// </summary>
    [RelayCommand]
    public async Task RestoreDefaultAppDataFolderAsync()
    {
        var dialog = _services.GetRequiredService<IMainWindowDialog>();
        IsMovingAppData = true;
        string? error;
        try
        {
            var defaultFolder = Path.Combine(Env.UserAppDataDirectory, Env.SoftName);
            Directory.CreateDirectory(defaultFolder);
            error = await CopyAppDataFilesAsync(defaultFolder);
            if (error is null)
            {
                try
                {
                    await Env.SaveCustomAppDataDirectoryAsync(string.Empty);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }
        }
        finally
        {
            IsMovingAppData = false;
            AppDataMoveProgress = string.Empty;
        }

        if (error is not null)
        {
            await dialog.ShowMessageAsync(Strings.AppDataFolder, error);
            return;
        }

        await dialog.ShowMessageAsync(Strings.AppDataFolder, Strings.RestoreDefaultFolderConfirm);
        RestartApp();
    }

    private static void RestartApp()
    {
        if (string.IsNullOrEmpty(Env.ProgramPath)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Env.ProgramPath,
                UseShellExecute = true,
                Arguments = StartArguments.ShutdownPrivious
            });
        }
        catch { }
    }

    /// <summary>
    /// Copies app data files to the target folder (without touching the config).
    /// Returns null on success, or a user-readable error message on failure.
    /// </summary>
    private async Task<string?> CopyAppDataFilesAsync(string targetFolder)
    {
        AppDataMoveProgress = Strings.CalculatingSize;
        var sourceDir = new DirectoryInfo(Env.AppDataDirectory);
        long sourceSize = await Task.Run(() => GetDirectorySize(sourceDir));

        var rootPath = Path.GetPathRoot(targetFolder);
        if (rootPath is null)
        {
            return Strings.NotEnoughDiskSpace;
        }

        var drive = new DriveInfo(rootPath);
        long availableBytes = drive.AvailableFreeSpace;
        if (availableBytes < sourceSize)
        {
            long requiredMB = sourceSize / (1024 * 1024) + 1;
            long availableMB = availableBytes / (1024 * 1024);
            return string.Format(Strings.NotEnoughDiskSpace, requiredMB, availableMB);
        }

        var progress = new Progress<int>(pct =>
            AppDataMoveProgress = string.Format(Strings.CopyingData, pct));

        try
        {
            await Task.Run(() => CopyDirectoryWithProgress(Env.AppDataDirectory, targetFolder, sourceSize, progress));
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        return null;
    }

    /// <summary>
    /// Copies app data to the target folder and saves the custom path config.
    /// Returns null on success, or a user-readable error message on failure.
    /// </summary>
    private async Task<string?> MoveAppDataFolderAsync(string targetFolder)
    {
        var copyError = await CopyAppDataFilesAsync(targetFolder);
        if (copyError is not null) return copyError;

        // Write to the independent config file in the default AppData location.
        // This file must never be in the custom path, as it determines that path at startup.
        try
        {
            await Env.SaveCustomAppDataDirectoryAsync(targetFolder);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        return null;
    }

    private static long GetDirectorySize(DirectoryInfo directory)
    {
        long size = 0;
        foreach (var folderName in Env.AppDataCopyWhitelistFolders)
        {
            var subDir = new DirectoryInfo(Path.Combine(directory.FullName, folderName));
            if (subDir.Exists)
            {
                foreach (var file in subDir.EnumerateFiles("*", SearchOption.AllDirectories))
                    size += file.Length;
            }
        }
        foreach (var fileName in Env.AppDataCopyWhitelistFiles)
        {
            var file = new FileInfo(Path.Combine(directory.FullName, fileName));
            if (file.Exists)
                size += file.Length;
        }
        return size;
    }

    private static void CopyDirectoryWithProgress(string source, string destination, long totalBytes, IProgress<int> progress)
    {
        Directory.CreateDirectory(destination);
        long copiedBytes = 0;
        int lastReported = -1;

        void CopyFile(string srcFile)
        {
            var relativePath = Path.GetRelativePath(source, srcFile);
            var destFile = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(srcFile, destFile, overwrite: true);
            copiedBytes += new FileInfo(srcFile).Length;
            int pct = totalBytes > 0 ? (int)(copiedBytes * 100 / totalBytes) : 100;
            if (pct != lastReported)
            {
                lastReported = pct;
                progress.Report(pct);
            }
        }

        foreach (var folderName in Env.AppDataCopyWhitelistFolders)
        {
            var subDir = Path.Combine(source, folderName);
            if (Directory.Exists(subDir))
            {
                foreach (var file in Directory.EnumerateFiles(subDir, "*", SearchOption.AllDirectories))
                    CopyFile(file);
            }
        }
        foreach (var fileName in Env.AppDataCopyWhitelistFiles)
        {
            var filePath = Path.Combine(source, fileName);
            if (File.Exists(filePath))
                CopyFile(filePath);
        }
        progress.Report(100);
    }
}
