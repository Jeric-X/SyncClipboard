using System;
using SyncClipboard.Utility;
using SyncClipboard.Utility.Notification;
using SyncClipboard.Utility.Web;
#nullable enable

namespace SyncClipboard.Service
{
    public class ProgressToastReporter : IProgress<HttpDownloadProgress>
    {
        private readonly ProgressBar _progressBar;
        private readonly Counter _counter;
        public ProgressToastReporter(string filename, string title)
        {
            _progressBar = new(title)
            {
                Tag = title + filename,
                ProgressTitle = filename,
                ProgressStatus = "当前状态",
                ProgressValue = 0,
                ProgressValueTip = "准备下载"
            };
            _progressBar.ShowSilent();
            _counter = new Counter((_) => _progressBar.Upadate(), 500, ulong.MaxValue);
        }

        public void Cancel()
        {
            _counter.Cancle();
            _progressBar.ProgressValueTip = "已取消";
            _progressBar.Upadate();
        }

        public void CancelSicent()
        {
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
                _counter.Cancle();
                _progressBar.Remove();
            }
        }
    }
}
