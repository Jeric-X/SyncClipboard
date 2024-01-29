namespace SyncClipboard.Abstract;

public interface IProgressBar : INotificationSession
{
    public string? ProgressTitle { get; set; }
    public double? ProgressValue { get; set; }
    public bool IsIndeterminate { get; set; }
    public string? ProgressValueTip { get; set; }
    public string ProgressStatus { get; set; }

    public void ShowSilent();
    public bool Upadate();
    public void Remove();
}
