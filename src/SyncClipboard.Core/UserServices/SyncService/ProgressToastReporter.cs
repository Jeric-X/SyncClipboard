using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.UserServices;

public class ProgressToastReporter : IProgress<HttpDownloadProgress>
{
    private readonly IProgressBar _progressBar;
    private readonly Counter _counter;
    public ProgressToastReporter(string filename, string title, INotification notificationManager, params Button[] buttons)
    {
        _progressBar = notificationManager.CreateProgressNotification(title);
        _progressBar.ProgressTitle = filename;
        _progressBar.ProgressStatus = I18n.Strings.DownloadStatus;
        _progressBar.ProgressValue = 0;
        _progressBar.ProgressValueTip = I18n.Strings.Preparing;
        _progressBar.Buttons = buttons.ToList();

        AppCore.Current.Logger.Write("ProgressToastReporter created");
        _progressBar.ShowSilent();
        _counter = new Counter((_) => _progressBar.Upadate(), 500, ulong.MaxValue);
    }

    public void Cancel()
    {
        AppCore.Current.Logger.Write("ProgressToastReporter status set to cancel");
        _counter.Cancle();
        _progressBar.ProgressValueTip = I18n.Strings.Canceled;
        _progressBar.Upadate();
    }

    public void CancelSicent()
    {
        AppCore.Current.Logger.Write("ProgressToastReporter canceled sicently");
        _counter.Cancle();
        _progressBar.Remove();
    }

    public void Report(HttpDownloadProgress progress)
    {
        var percent = (double)progress.BytesReceived / progress.TotalBytesToReceive;
        if (percent < _progressBar.ProgressValue)
        {
            return;
        }
        _progressBar.ProgressValue = percent;
        _progressBar.ProgressValueTip = percent?.ToString("P");
        if (progress.End)
        {
            AppCore.Current.Logger.Write("ProgressToastReporter removed due to complete");
            _counter.Cancle();
            _progressBar.Remove();
        }
    }
}
