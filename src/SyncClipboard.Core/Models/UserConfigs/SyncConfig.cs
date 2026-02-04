namespace SyncClipboard.Core.Models.UserConfigs;

public record class SyncConfig
{
    public bool SyncSwitchOn { get; set; } = false;
    public bool PullSwitchOn { get; set; } = true;
    public bool PushSwitchOn { get; set; } = true;
    public bool EnableUploadText { get; set; } = true;
    public bool EnableUploadSingleFile { get; set; } = true;
    public bool EnableUploadMultiFile { get; set; } = true;
    public bool NotifyOnDownloaded { get; set; } = true;
    public bool DoNotUploadWhenCut { get; set; } = false;
    public bool NotifyOnManualUpload { get; set; } = false;
    public bool NotifyFileSyncProgress { get; set; } = true;
    public bool TrustInsecureCertificate { get; set; } = false;
    public bool IgnoreExcludeForSyncSuggestion { get; set; } = false;
    public uint MaxFileByte { get; set; } = 1024 * 1024 * 20;  // 20MB 
    public uint IntervalTime { get; set; } = 3;
    public uint RetryTimes { get; set; } = 3;
    public uint TimeOut { get; set; } = 100;
}
