namespace SyncClipboard.Core.Models.UserConfigs;

public record class SyncConfig
{
    public string RemoteURL { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public bool SyncSwitchOn { get; set; } = false;
    public bool PullSwitchOn { get; set; } = true;
    public bool PushSwitchOn { get; set; } = true;
    public bool EnableUploadText { get; set; } = true;
    public bool EnableUploadSingleFile { get; set; } = true;
    public bool EnableUploadMultiFile { get; set; } = true;
    public bool UseLocalServer { get; set; } = false;
    public bool DeletePreviousFilesOnPush { get; set; } = true;
    public bool NotifyOnDownloaded { get; set; } = false;
    public bool DoNotUploadWhenCut { get; set; } = false;
    public bool NotifyOnManualUpload { get; set; } = false;
    public bool TrustInsecureCertificate { get; set; } = false;
    public uint MaxFileByte { get; set; } = 1024 * 1024 * 20;  // 20MB 
    public uint IntervalTime { get; set; } = 3;
    public uint RetryTimes { get; set; } = 3;
    public uint TimeOut { get; set; } = 100;
}
