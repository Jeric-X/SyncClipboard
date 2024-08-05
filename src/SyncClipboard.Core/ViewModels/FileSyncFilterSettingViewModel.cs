using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.ViewModels;

public partial class FileSyncFilterSettingViewModel : ObservableObject
{
    public static readonly LocaleString[] Modes =
    {
        new ("", Strings.None),
        new ("BlackList", Strings.BlackList),
        new ("WhiteList", Strings.WhiteList)
    };

    [ObservableProperty]
    private LocaleString filterMode = Modes[0];
    partial void OnFilterModeChanged(LocaleString value) => FilterConfig = FilterConfig with { FileFilterMode = value.String };

    [ObservableProperty]
    private string blackList = "";
    partial void OnBlackListChanged(string value)
    {
        FilterConfig = FilterConfig with { BlackList = value.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.RemoveEmptyEntries).ToList() };
    }

    [ObservableProperty]
    private string whiteList = "";
    partial void OnWhiteListChanged(string value)
    {
        FilterConfig = FilterConfig with { WhiteList = value.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.RemoveEmptyEntries).ToList() };
    }

    [ObservableProperty]
    private FileFilterConfig filterConfig = new();
    partial void OnFilterConfigChanged(FileFilterConfig value)
    {
        FilterMode = Modes.FirstOrDefault(x => x.String == FilterConfig.FileFilterMode) ?? Modes[0];
        BlackList = string.Join(Environment.NewLine, value.BlackList);
        WhiteList = string.Join(Environment.NewLine, value.WhiteList);
        _configManager.SetConfig(value);
    }

    private readonly ConfigManager _configManager;

    public FileSyncFilterSettingViewModel(ConfigManager configManager)
    {
        _configManager = configManager;
        configManager.GetAndListenConfig<FileFilterConfig>(config => FilterConfig = config);
    }
}
