using SyncClipboard.Abstract;

namespace SyncClipboard.Desktop.Utilities;

internal class ProgressBar : IProgressBar
{
    public string Tag { get; set; } = "";
    public string? ProgressTitle { get; set; }
    public double? ProgressValue { get; set; }
    public bool IsIndeterminate { get; set; }
    public string? ProgressValueTip { get; set; }
    public string ProgressStatus { get; set; } = "";

    public void Remove()
    {
    }

    public void ShowSilent()
    {
    }

    public bool Upadate()
    {
        return true;
    }
}
