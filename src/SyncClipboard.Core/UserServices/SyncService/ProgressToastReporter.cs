using NativeNotification.Interface;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.UserServices;

public class ProgressToastReporter : IProgress<HttpDownloadProgress>
{
    private readonly IProgressNotification _progressBar;
    private readonly Counter _counter;
    private readonly Action<HttpDownloadProgress>? _action;
    private HttpDownloadProgress _progress;

    public ProgressToastReporter(string filename, string title, INotificationManager notificationManager, Action<HttpDownloadProgress>? action, params ActionButton[] buttons)
    {
        _progressBar = notificationManager.CreateProgress(true);
        _action = action;
        _progressBar.Title = title;
        _progressBar.ProgressTitle = filename;
        //_progressBar.ProgressStatus = I18n.Strings.DownloadStatus;
        _progressBar.ProgressValue = 0;
        _progressBar.ProgressValueTip = I18n.Strings.Preparing;
        _progressBar.Buttons = buttons.ToList();

        AppCore.Current.Logger.Write("ProgressToastReporter created");
        _progressBar.Show(new NotificationDeliverOption { Silent = true });
        _counter = new Counter((_) => UpdateProgress(), 500, ulong.MaxValue);
    }

    private void UpdateProgress()
    {
        _action?.Invoke(_progress);

        var percent = (double)_progress.BytesReceived / _progress.TotalBytesToReceive;
        if (percent < _progressBar.ProgressValue)
        {
            return;
        }
        _progressBar.ProgressValue = percent;
        _progressBar.ProgressValueTip = percent?.ToString("P");
        if (_progress.End)
        {
            AppCore.Current.Logger.Write("ProgressToastReporter removed due to complete");
            _counter.Cancle();
            _progressBar.Remove();
        }
        _progressBar.Update();
    }

    public void Cancel()
    {
        AppCore.Current.Logger.Write("ProgressToastReporter status set to cancel");
        _counter.Cancle();
        _progressBar.ProgressValueTip = I18n.Strings.Canceled;
        _progressBar.Update();
    }

    public void CancelSicent()
    {
        AppCore.Current.Logger.Write("ProgressToastReporter canceled sicently");
        _counter.Cancle();
        _progressBar.Remove();
    }

    public void Report(HttpDownloadProgress progress)
    {
        _progress = progress;
    }
}
