using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.ViewModels;

public partial class FileSyncFilterSettingViewModel : ObservableObject
{
    public static readonly LocaleString[] Modes =
    [
        new ("", Strings.None),
        new ("BlackList", Strings.BlackList),
        new ("WhiteList", Strings.WhiteList)
    ];

    [ObservableProperty]
    private LocaleString filterMode = Modes[0];
    partial void OnFilterModeChanged(LocaleString value) => FilterConfig = FilterConfig with { FileFilterMode = value.String };

    [ObservableProperty]
    private FileFilterConfig filterConfig = new();
    partial void OnFilterConfigChanged(FileFilterConfig value)
    {
        FilterMode = Modes.FirstOrDefault(x => x.String == FilterConfig.FileFilterMode) ?? Modes[0];
        if (FilterConfig.FileFilterMode == "BlackList")
        {
            ShownText = string.Join(Environment.NewLine, value.BlackList);
            EnableText = true;
        }
        else if (FilterConfig.FileFilterMode == "WhiteList")
        {
            ShownText = string.Join(Environment.NewLine, value.WhiteList);
            EnableText = true;
        }
        else
        {
            ShownText = "";
            EnableText = false;
        }
        _configManager.SetConfig(value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private bool enableText = false;

    [ObservableProperty]
    private string shownText = "";

    public string? Description => EnableText ? I18n.Strings.FileFilterDescription : null;

    [RelayCommand]
    public void Apply()
    {
        var list = ShownText.Split(["\r\n", "\r", "\n"],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
        list.Sort();
        if (FilterConfig.FileFilterMode == "BlackList")
        {
            FilterConfig = FilterConfig with { BlackList = list };
        }
        else if (FilterConfig.FileFilterMode == "WhiteList")
        {
            FilterConfig = FilterConfig with { WhiteList = list };
        }
        ShownText = string.Join(Environment.NewLine, list);
    }

    [RelayCommand]
    public void Confirm()
    {
        Apply();
        AppCore.Current.Services.GetRequiredService<MainViewModel>().NavigateToLastLevel();
    }

    private readonly ConfigManager _configManager;

    public FileSyncFilterSettingViewModel(ConfigManager configManager)
    {
        _configManager = configManager;
        configManager.GetAndListenConfig<FileFilterConfig>(config => FilterConfig = config);
    }
}
