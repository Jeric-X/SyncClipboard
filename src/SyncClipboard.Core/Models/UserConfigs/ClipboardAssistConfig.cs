﻿namespace SyncClipboard.Core.Models.UserConfigs;

public record class ClipboardAssistConfig
{
    public bool EasyCopyImageSwitchOn { get; set; } = false;
    public bool DownloadWebImage { get; set; } = false;
    public bool ConvertSwitchOn { get; set; } = false;
}