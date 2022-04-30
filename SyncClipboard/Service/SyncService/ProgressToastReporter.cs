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
        public ProgressToastReporter(string filename)
        {
            _progressBar = new("正在同步剪切板")
            {
                Tag = "Download progress",
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

        public void Report(HttpDownloadProgress progress)
        {
            _progressBar.ProgressValue = (double)progress.BytesReceived / progress.TotalBytesToReceive;
            _progressBar.ProgressValueTip = ((double)progress.BytesReceived / progress.TotalBytesToReceive)?.ToString("P");
            if (progress.End)
            {
                _counter.Cancle();
                _progressBar.Remove();
            }
        }
    }
}
