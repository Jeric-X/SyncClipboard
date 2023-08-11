namespace SyncClipboard.Core.Interfaces;

public interface ITrayIcon
{
    void Create();
    event Action MainWindowWakedUp;

    void ShowUploadAnimation();
    void ShowDownloadAnimation();
    void StopAnimation();

    public void SetStatusString(string key, string statusStr, bool error);
    public void SetStatusString(string key, string statusStr);
}
