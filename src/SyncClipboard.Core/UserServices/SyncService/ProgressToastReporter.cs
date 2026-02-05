using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.UserServices;

public class ProgressToastReporter : IProgress<HttpDownloadProgress>
{
    private readonly IProgressNotification? _progressBar;
    private readonly ITrayIcon? _trayIcon;
    private readonly Counter _counter;
    private HttpDownloadProgress _progress;

    public bool UseToast { get; }
    public bool UseTrayIcon { get; }
    public string ServiceName { get; }

    public ProgressToastReporter(string serviceName, string filename, string title, bool useToast = true, bool useTrayIcon = true, params ActionButton[] buttons)
    {
        ServiceName = serviceName;
        UseToast = useToast;
        UseTrayIcon = useTrayIcon;

        if (UseTrayIcon)
        {
            _trayIcon = AppCore.Current.Services.GetRequiredService<ITrayIcon>();
        }

        if (UseToast)
        {
            var notificationManager = AppCore.Current.Services.GetRequiredService<INotificationManager>();
            _progressBar = notificationManager.CreateProgress(true);
            _progressBar.Title = serviceName;
            _progressBar.Message = filename;
            _progressBar.ProgressTitle = title;
            //_progressBar.ProgressStatus = I18n.Strings.DownloadStatus;
            _progressBar.ProgressValue = 0;
            _progressBar.IsIndeterminate = true;
            _progressBar.ProgressValueTip = I18n.Strings.Preparing;
            _progressBar.Buttons = buttons.ToList();
            _progressBar.Show(new NotificationDeliverOption { Silent = true });
        }
        _counter = new Counter((_) => UpdateProgress(), 500, ulong.MaxValue);
    }

    private void UpdateProgress()
    {
        double? percentNullable = (double)_progress.BytesReceived / _progress.TotalBytesToReceive;
        if (!percentNullable.HasValue || double.IsNaN(percentNullable.Value) ||
            percentNullable.Value <= 0.01)
        {
            return;
        }

        double percent = percentNullable.Value;
        if (UseTrayIcon)
        {
            UpdateTrayIcon(percent);
        }

        if (UseToast)
        {
            UpdateNotification(percent);
        }
    }

    private void UpdateTrayIcon(double percent)
    {
        ArgumentNullException.ThrowIfNull(_trayIcon);
        _trayIcon.SetStatusString(ServiceName, $"{percent:P}");
    }

    private void UpdateNotification(double percent)
    {
        ArgumentNullException.ThrowIfNull(_progressBar);

        _progressBar.ProgressValue = percent;
        _progressBar.ProgressValueTip = percent.ToString("P");
        //_progressBar.ProgressTitle = percent.ToString("P");

        if (_progress.End)
        {
            _counter.Cancle();
            if (UseToast)
            {
                _progressBar.Remove();
            }
        }
        else if (_progressBar.IsIndeterminate)
        {
            if (UseToast)
            {
                _progressBar.Remove();
                _progressBar.IsIndeterminate = false;
                _progressBar.Show(new NotificationDeliverOption { Silent = true });
            }
        }
        else
        {
            if (UseToast)
            {
                _progressBar.Update();
            }
        }
    }

    public void Cancel()
    {
        AppCore.Current.Logger.Write("ProgressToastReporter status set to cancel");
        _counter.Cancle();
        if (UseToast)
        {
            ArgumentNullException.ThrowIfNull(_progressBar);
            _progressBar.ProgressValueTip = I18n.Strings.Canceled;
            //_progressBar.ProgressTitle = I18n.Strings.Canceled;
            _progressBar.Update();
        }
    }

    public void CancelSicent()
    {
        AppCore.Current.Logger.Write("ProgressToastReporter canceled sicently");
        _counter.Cancle();
        if (UseToast)
        {
            ArgumentNullException.ThrowIfNull(_progressBar);
            _progressBar.Remove();
        }
    }

    public void Report(HttpDownloadProgress progress)
    {
        _progress = progress;
    }
}
