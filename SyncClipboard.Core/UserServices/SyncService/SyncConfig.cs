namespace SyncClipboard.Core.UserServices;

public record class SyncConfig
{
    public string RemoteURL { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public bool SyncSwitchOn { get; set; } = false;
    public bool PullSwitchOn { get; set; } = false;
    public bool PushSwitchOn { get; set; } = false;
    public bool DeletePreviousFilesOnPush { get; set; } = true;
    public bool EasyCopyImageSwitchOn { get; set; } = false;
    public int MaxFileByte { get; set; } = 1024 * 1024 * 20;  // 20MB
}
