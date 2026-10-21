using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.ViewModels;

public partial class SystemSettingViewModel
{
    private bool _isLoadingPerformanceConfig;

    [ObservableProperty]
    private uint historyImageCacheSizeMB;
    partial void OnHistoryImageCacheSizeMBChanged(uint value)
    {
        if (!_isLoadingPerformanceConfig)
            _configManager.SetConfig(_configManager.GetConfig<PerformanceConfig>() with { HistoryImageCacheSizeMB = value });
    }

    [ObservableProperty]
    private int historyImageLoadConcurrency;
    partial void OnHistoryImageLoadConcurrencyChanged(int value)
    {
        if (!_isLoadingPerformanceConfig)
        {
            var limit = Math.Max(1, value);
            _configManager.SetConfig(_configManager.GetConfig<PerformanceConfig>() with { HistoryImageLoadConcurrency = limit });
            HistoryImageLoadConcurrency = limit;
        }
    }

    private void LoadPerformanceConfig(PerformanceConfig config)
    {
        _isLoadingPerformanceConfig = true;
        try
        {
            HistoryImageCacheSizeMB = config.HistoryImageCacheSizeMB;
            HistoryImageLoadConcurrency = Math.Max(1, config.HistoryImageLoadConcurrency);
        }
        finally
        {
            _isLoadingPerformanceConfig = false;
        }
    }
}
